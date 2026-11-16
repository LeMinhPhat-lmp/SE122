using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AERS.AgentClient;

/// <summary>PROVISIONAL (ADR-W9-03): persists one credential JSON file per agent key.</summary>
public sealed class FileCredentialStore(string? directory = null) : ICredentialStore
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
  };
  private readonly string directory = directory ?? Path.Combine(".", ".aers-agents");

  public async Task<StoredCredential?> LoadAsync(string agentKey, CancellationToken cancellationToken = default)
  {
    try
    {
      var path = PathFor(agentKey);
      if (!File.Exists(path) || new FileInfo(path).Length == 0)
        return null;
      await using var stream = File.OpenRead(path);
      return await JsonSerializer.DeserializeAsync<StoredCredential>(stream, JsonOptions, cancellationToken);
    }
    catch (IOException) { return null; }
    catch (UnauthorizedAccessException) { return null; }
    catch (JsonException) { return null; }
  }

  public async Task SaveAsync(string agentKey, StoredCredential credential, CancellationToken cancellationToken = default)
  {
    Directory.CreateDirectory(directory);
    var path = PathFor(agentKey);
    var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
    try
    {
      await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
        4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
      {
        await JsonSerializer.SerializeAsync(stream, credential, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
      }
      TryRestrictPermissions(temporary);
      File.Move(temporary, path, true);
    }
    finally
    {
      if (File.Exists(temporary))
        File.Delete(temporary);
    }
  }

  public Task DeleteAsync(string agentKey, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    var path = PathFor(agentKey);
    if (File.Exists(path))
      File.Delete(path);
    return Task.CompletedTask;
  }

  private string PathFor(string agentKey)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(agentKey);
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(agentKey))).ToLowerInvariant();
    return Path.Combine(directory, $"{hash}.json");
  }

  private static void TryRestrictPermissions(string path)
  {
    if (!OperatingSystem.IsWindows())
      File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
  }
}

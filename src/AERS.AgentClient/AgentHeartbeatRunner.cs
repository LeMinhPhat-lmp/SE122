using Microsoft.Extensions.Logging;

namespace AERS.AgentClient;

public sealed class AgentHeartbeatRunner(
  AersClient client,
  ILogger<AgentHeartbeatRunner>? logger = null)
{
  public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(15);

  private readonly ILogger<AgentHeartbeatRunner>? logger = logger;

  public Task RunAsync(Guid agentId, CancellationToken ct = default) =>
    RunAsync(agentId, DefaultInterval, ct);

  public async Task RunAsync(Guid agentId, TimeSpan interval, CancellationToken ct = default)
  {
    if (interval <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(interval));

    while (true)
    {
      try
      {
        await client.SendHeartbeatAsync(agentId, ct);
      }
      catch (AersApiException ex) when (ex.StatusCode >= 500)
      {
        logger?.LogWarning("Heartbeat failed with a transient server error; retrying.");
      }
      catch (HttpRequestException)
      {
        logger?.LogWarning("Heartbeat failed due to a transient network error; retrying.");
      }

      await Task.Delay(interval, ct);
    }
  }
}

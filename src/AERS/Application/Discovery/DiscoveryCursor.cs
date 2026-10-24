using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Interfaces.Repositories;

namespace Application.Discovery;

/// <summary>
/// Opaque pagination cursor of GET /agents: base64url (no padding) of {"v":1,"t":"&lt;UTC ISO-8601, 6 digits&gt;","id":"&lt;uuid&gt;"}.
/// The cursor is not signed; scope and access are re-evaluated on every request.
/// </summary>
public static class DiscoveryCursor
{
  private const int Version = 1;
  private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'";

  internal sealed record Payload(
    [property: JsonPropertyName("v")] int? V,
    [property: JsonPropertyName("t")] string? T,
    [property: JsonPropertyName("id")] string? Id);

  public static string Encode(DiscoveryCursorPosition position)
  {
    var payload = new Payload(
      Version,
      position.RegisteredAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture),
      position.Id.ToString("D"));

    return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
  }

  public static bool TryDecode(string value, out DiscoveryCursorPosition position)
  {
    position = default!;

    try
    {
      var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value)));

      if (payload is not { V: Version, T: not null, Id: not null }
          || !DateTimeOffset.TryParseExact(payload.T, TimestampFormat, CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var registeredAt)
          || !Guid.TryParseExact(payload.Id, "D", out var id))
        return false;

      position = new DiscoveryCursorPosition(registeredAt, id);
      return true;
    }
    catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
    {
      return false;
    }
  }
}

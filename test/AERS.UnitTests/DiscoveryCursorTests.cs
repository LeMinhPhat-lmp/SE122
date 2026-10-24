using System.Buffers.Text;
using System.Text;
using Application.Discovery;
using Application.Interfaces.Repositories;
using Xunit;

namespace AERS.UnitTests;

public sealed class DiscoveryCursorTests
{
  private static string EncodeRaw(string json) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(json));

  [Fact]
  public void Encode_then_decode_returns_the_same_position()
  {
    var position = new DiscoveryCursorPosition(new DateTimeOffset(2026, 10, 1, 8, 30, 15, TimeSpan.Zero), Guid.NewGuid());

    var decoded = DiscoveryCursor.TryDecode(DiscoveryCursor.Encode(position), out var result);

    Assert.True(decoded);
    Assert.Equal(position, result);
  }

  [Fact]
  public void Microsecond_precision_is_preserved()
  {
    // 123456 microseconds = 1234560 ticks (100 ns each).
    var timestamp = new DateTimeOffset(2026, 10, 1, 8, 30, 15, TimeSpan.Zero).AddTicks(1234560);
    var position = new DiscoveryCursorPosition(timestamp, Guid.NewGuid());

    Assert.True(DiscoveryCursor.TryDecode(DiscoveryCursor.Encode(position), out var result));
    Assert.Equal(timestamp, result.RegisteredAt);
  }

  [Fact]
  public void Non_utc_input_is_encoded_as_utc()
  {
    var local = new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.FromHours(7));

    Assert.True(DiscoveryCursor.TryDecode(DiscoveryCursor.Encode(new DiscoveryCursorPosition(local, Guid.NewGuid())), out var result));
    Assert.Equal(local.ToUniversalTime(), result.RegisteredAt);
    Assert.Equal(TimeSpan.Zero, result.RegisteredAt.Offset);
  }

  [Fact]
  public void Encoded_cursor_is_unpadded_base64url_of_the_documented_json()
  {
    var id = Guid.Parse("3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85");
    var cursor = DiscoveryCursor.Encode(new DiscoveryCursorPosition(new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero), id));

    Assert.DoesNotContain('=', cursor);
    Assert.DoesNotContain('+', cursor);
    Assert.DoesNotContain('/', cursor);
    var json = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
    Assert.Equal("{\"v\":1,\"t\":\"2026-10-01T08:00:00.000000Z\",\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}", json);
  }

  public static TheoryData<string> MalformedCursors => new()
  {
    "",
    "not base64 !!",
    "e30=",                                                                                // padded base64
    EncodeRaw("not json"),
    EncodeRaw("null"),
    EncodeRaw("[]"),
    EncodeRaw("{}"),
    EncodeRaw("{\"v\":2,\"t\":\"2026-10-01T08:00:00.000000Z\",\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}"),
    EncodeRaw("{\"t\":\"2026-10-01T08:00:00.000000Z\",\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}"),
    EncodeRaw("{\"v\":1,\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}"),
    EncodeRaw("{\"v\":1,\"t\":\"2026-10-01T08:00:00.000000Z\"}"),
    EncodeRaw("{\"v\":1,\"t\":\"yesterday\",\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}"),
    EncodeRaw("{\"v\":1,\"t\":\"2026-10-01T08:00:00Z\",\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}"), // not 6 fractional digits
    EncodeRaw("{\"v\":1,\"t\":\"2026-10-01T08:00:00.000000Z\",\"id\":\"not-a-guid\"}"),
    EncodeRaw("{\"v\":1,\"t\":5,\"id\":\"3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85\"}"),
  };

  [Theory]
  [MemberData(nameof(MalformedCursors))]
  public void Malformed_cursor_is_rejected(string cursor)
  {
    Assert.False(DiscoveryCursor.TryDecode(cursor, out _));
  }
}

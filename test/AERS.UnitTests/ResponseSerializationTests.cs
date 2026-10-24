using System.Text.Json;
using Application.DTOs.Agent.Responses;
using Xunit;

namespace AERS.UnitTests;

// The API layer does not configure null omission, so web defaults are what the endpoints use.
public sealed class ResponseSerializationTests
{
  [Fact]
  public void Discovery_response_serializes_next_cursor_and_owner_ref_as_explicit_null()
  {
    var schema = JsonDocument.Parse("{}").RootElement.Clone();
    var card = new AgentCardResponse(Guid.NewGuid(), "n", "jobhunt", "d", [new CapabilityResponse("c", "d", schema, schema)], null);

    using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AgentDiscoveryResponse([card], null), JsonSerializerOptions.Web));

    Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
    Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("agents")[0].GetProperty("ownerRef").ValueKind);
  }

  [Fact]
  public void Capabilities_response_uses_camel_case_names()
  {
    using var json = JsonDocument.Parse(JsonSerializer.Serialize(new AgentCapabilitiesResponse(Guid.NewGuid(), []), JsonSerializerOptions.Web));

    Assert.True(json.RootElement.TryGetProperty("agentId", out _));
    Assert.True(json.RootElement.TryGetProperty("capabilities", out _));
  }
}

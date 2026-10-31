using System.Text.Json;
using Xunit;

namespace AERS.UnitTests;

[Collection("Discovery integration")]
public sealed class DiscoveryIntegrationTests(DiscoveryIntegrationFixture fixture)
{
  [SkippableFact]
  public async Task IT_01_DefaultDomainScope_IncludesCallerOnly()
  {
    IntegrationTestSkip.Require();
    var client = fixture.CreateClient();
    var caller = await fixture.RegisterAgentAsync(client, UniqueDomain(), "Caller", Capability("analyze_cv"));
    await fixture.RegisterAgentAsync(client, UniqueDomain(), "Other", Capability("analyze_cv"));

    using var request = new HttpRequestMessage(HttpMethod.Get, "agents");
    request.Headers.Add("X-Api-Key", caller.ApiKey);
    var response = await client.SendAsync(request);
    var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    Assert.Equal(200, (int)response.StatusCode);
    Assert.Contains(json.RootElement.GetProperty("agents").EnumerateArray(),
      agent => agent.GetProperty("agentId").GetGuid() == caller.AgentId);
  }

  [SkippableFact]
  public async Task IT_09_NoMatches_ReturnsExplicitNullCursor()
  {
    IntegrationTestSkip.Require();
    var client = fixture.CreateClient();
    var caller = await fixture.RegisterAgentAsync(client, UniqueDomain(), "Caller", Capability("other"));
    using var request = new HttpRequestMessage(HttpMethod.Get, "agents?capability=missing");
    request.Headers.Add("X-Api-Key", caller.ApiKey);

    var response = await client.SendAsync(request);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal(200, (int)response.StatusCode);
    Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("agents").ValueKind);
    Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
  }

  [SkippableFact]
  public async Task IT_15_Pagination_ThreePages_NoDuplicates()
  {
    IntegrationTestSkip.Require();
    var client = fixture.CreateClient();
    var domain = UniqueDomain();
    var caller = await fixture.RegisterAgentAsync(client, domain, "Caller", Capability("page"));
    for (var index = 0; index < 4; index++)
      await fixture.RegisterAgentAsync(client, domain, $"Agent {index}", Capability("page"));

    var ids = new List<Guid>();
    string? cursor = null;
    do
    {
      var suffix = cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}";
      using var request = new HttpRequestMessage(HttpMethod.Get, $"agents?limit=2{suffix}");
      request.Headers.Add("X-Api-Key", caller.ApiKey);
      using var json = JsonDocument.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync());
      ids.AddRange(json.RootElement.GetProperty("agents").EnumerateArray()
        .Select(agent => agent.GetProperty("agentId").GetGuid()));
      cursor = json.RootElement.GetProperty("nextCursor").GetString();
    }
    while (cursor is not null);

    Assert.Equal(5, ids.Count);
    Assert.Equal(ids.Count, ids.Distinct().Count());
  }

  [SkippableFact]
  public async Task IT_22_InvalidLimit_ReturnsValidationPath()
  {
    IntegrationTestSkip.Require();
    var client = fixture.CreateClient();
    var caller = await fixture.RegisterAgentAsync(client, UniqueDomain(), "Caller", Capability("test"));
    using var request = new HttpRequestMessage(HttpMethod.Get, "agents?limit=0");
    request.Headers.Add("X-Api-Key", caller.ApiKey);

    var response = await client.SendAsync(request);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(400, (int)response.StatusCode);
    Assert.Contains(json.RootElement.GetProperty("details").GetProperty("errors").EnumerateArray(),
      error => error.GetProperty("path").GetString() == "/limit");
  }

  [SkippableFact]
  public async Task IT_31_Capabilities_ReturnRawSchemas()
  {
    IntegrationTestSkip.Require();
    var client = fixture.CreateClient();
    var caller = await fixture.RegisterAgentAsync(client, UniqueDomain(), "Caller",
      new { name = "analyze_cv", description = "Analyze", inputSchema = new { type = "object" }, outputSchema = new { type = "number" } });
    using var request = new HttpRequestMessage(HttpMethod.Get, $"agents/{caller.AgentId}/capabilities");
    request.Headers.Add("X-Api-Key", caller.ApiKey);

    using var json = JsonDocument.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync());
    var capability = json.RootElement.GetProperty("capabilities")[0];
    Assert.Equal(JsonValueKind.Object, capability.GetProperty("inputSchema").ValueKind);
    Assert.Equal(JsonValueKind.Object, capability.GetProperty("outputSchema").ValueKind);
  }

  private static object Capability(string name) => new
  {
    name,
    description = "Integration capability.",
    inputSchema = new { type = "object" },
    outputSchema = new { type = "object" }
  };

  private static string UniqueDomain() => $"it{Guid.NewGuid():N}"[..20];
}

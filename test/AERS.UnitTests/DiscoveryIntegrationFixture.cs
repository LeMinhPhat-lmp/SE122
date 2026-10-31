using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Infrastructure.Persistences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace AERS.UnitTests;

public sealed class DiscoveryIntegrationFixture : IAsyncLifetime
{
  public const string RegistrationKey = "integration-registration-key";
  private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16")
    .WithDatabase("aers")
    .WithUsername("aers")
    .WithPassword("aers")
    .Build();
  private readonly RedisContainer redis = new RedisBuilder("redis:7")
    .Build();

  public WebApplicationFactory<Program> Factory { get; private set; } = null!;

  public async Task InitializeAsync()
  {
    if (IntegrationTestSkip.Reason is not null)
      return;

    await postgres.StartAsync();
    await redis.StartAsync();
    Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
      builder.UseEnvironment("Development");
      builder.UseSetting("Database:ConnectionString", postgres.GetConnectionString());
      builder.UseSetting("Aers:Redis:ConnectionString", redis.GetConnectionString());
      builder.UseSetting("Aers:Auth:RegistrationKeys:0", RegistrationKey);
      builder.UseSetting("Aers:Policy:DomainPolicy:Mode", "DenyByDefault");
    });
    using var scope = Factory.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<AersDbContext>().Database.EnsureCreatedAsync();
  }

  public async Task DisposeAsync()
  {
    Factory?.Dispose();
    await redis.DisposeAsync();
    await postgres.DisposeAsync();
  }

  public HttpClient CreateClient() => Factory.CreateClient();

  public async Task<RegisteredAgent> RegisterAgentAsync(
    HttpClient client,
    string domain,
    string name,
    params object[] capabilities)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, "agents/register")
    {
      Content = JsonContent.Create(new
      {
        agentCard = new
        {
          name,
          domain,
          description = "Integration test agent.",
          ownerRef = (string?)null,
          capabilities
        }
      })
    };
    request.Headers.Add("X-Registration-Key", RegistrationKey);
    var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
      throw new InvalidOperationException(
        $"Registration failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return new RegisteredAgent(
      document.RootElement.GetProperty("agentId").GetGuid(),
      document.RootElement.GetProperty("apiKey").GetString()!);
  }
}

public sealed record RegisteredAgent(Guid AgentId, string ApiKey);

[CollectionDefinition("Discovery integration")]
public sealed class DiscoveryIntegrationCollection : ICollectionFixture<DiscoveryIntegrationFixture>
{
}

internal static class IntegrationTestSkip
{
  public static string? Reason =>
    string.Equals(Environment.GetEnvironmentVariable("AERS_RUN_INTEGRATION_TESTS"), "1",
      StringComparison.Ordinal)
      ? null
      : "Set AERS_RUN_INTEGRATION_TESTS=1 to run PostgreSQL/Redis integration tests.";

  public static void Require()
  {
    if (Reason is not null)
      Xunit.Skip.If(true, Reason);
  }
}

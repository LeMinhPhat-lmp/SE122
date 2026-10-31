using System.Net;
using System.Text;
using AERS.AgentClient;
using Xunit;

namespace AERS.UnitTests;

public sealed class AgentClientTests
{
  [Fact]
  public async Task ListAllAgents_FollowsOpaqueCursorAndEmptyPages()
  {
    var requests = new List<HttpRequestMessage>();
    var responses = new Queue<string>([
      """{"agents":[{"agentId":"00000000-0000-0000-0000-000000000001","name":"one","domain":"hr","description":"","capabilities":[],"ownerRef":null}],"nextCursor":"first+/="}""",
      """{"agents":[],"nextCursor":"second"}""",
      """{"agents":[{"agentId":"00000000-0000-0000-0000-000000000002","name":"two","domain":"hr","description":"","capabilities":[],"ownerRef":null}],"nextCursor":null}"""
    ]);
    using var http = new HttpClient(new StubHandler(request =>
    {
      requests.Add(request);
      return Json(responses.Dequeue());
    }));
    var client = CreateClient(http);

    var agents = await client.ListAllAgentsAsync(new("hr", "cv analysis", true, 10)).ToListAsync();

    Assert.Equal(2, agents.Count);
    var firstQuery = requests[0].RequestUri?.Query ?? string.Empty;
    var secondQuery = requests[1].RequestUri?.Query ?? string.Empty;
    Assert.Contains("cursor=first%2B%2F%3D", secondQuery);
    Assert.Contains("onlineOnly=true", firstQuery);
    Assert.Contains("capability=cv%20analysis", firstQuery);
    Assert.DoesNotContain("cursor=", firstQuery);
    Assert.Contains("domain=hr", firstQuery);
  }

  [Fact]
  public async Task RegisterUsesRegistrationHeaderOnly_AndStoresApiKey()
  {
    HttpRequestMessage? captured = null;
    using var http = new HttpClient(new StubHandler(request =>
    {
      captured = request;
      return Json("""{"agentId":"00000000-0000-0000-0000-000000000001","apiKey":"issued"}""");
    }));
    var client = new AersClient(http, new()
    {
      BaseUrl = new("https://aers.test/"),
      RegistrationKey = "registration",
      ApiKey = "old"
    });

    var result = await client.RegisterAsync(new("name", "hr", "description", null, []));

    Assert.Equal("issued", result.ApiKey);
    Assert.Equal("registration", captured!.Headers.GetValues("X-Registration-Key").Single());
    Assert.False(captured.Headers.Contains("X-Api-Key"));
  }

  [Fact]
  public async Task ErrorBodyMapsToAersApiException()
  {
    using var http = new HttpClient(new StubHandler(_ =>
      new(HttpStatusCode.BadRequest)
      {
        Content = new StringContent("""{"code":"VALIDATION_ERROR","message":"invalid","details":{"errors":[]}}""",
          Encoding.UTF8, "application/json")
      }));
    var client = CreateClient(http);

    var exception = await Assert.ThrowsAsync<AersApiException>(() =>
      client.ListAgentsAsync(new(null, null, false, null)));

    Assert.Equal(400, exception.StatusCode);
    Assert.Equal("VALIDATION_ERROR", exception.Code);
    Assert.Equal("invalid", exception.Message);
    Assert.NotNull(exception.Details);
  }

  [Fact]
  public async Task NonJsonErrorUsesUnknownError()
  {
    using var http = new HttpClient(new StubHandler(_ =>
      new(HttpStatusCode.NotFound) { Content = new StringContent("not json") }));
    var exception = await Assert.ThrowsAsync<AersApiException>(() =>
      CreateClient(http).GetAgentAsync(Guid.Empty));

    Assert.Equal(404, exception.StatusCode);
    Assert.Equal("UNKNOWN_ERROR", exception.Code);
  }

  [Fact]
  public async Task HeartbeatRunnerContinuesAfterServerError()
  {
    using var cancellation = new CancellationTokenSource();
    var calls = 0;
    using var http = new HttpClient(new StubHandler(_ =>
    {
      calls++;
      if (calls == 1)
        return new(HttpStatusCode.ServiceUnavailable);
      cancellation.Cancel();
      return new(HttpStatusCode.NoContent);
    }));
    var client = CreateClient(http);
    var runner = new AgentHeartbeatRunner(client);

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
      runner.RunAsync(Guid.Empty, TimeSpan.FromMilliseconds(1), cancellation.Token));

    Assert.Equal(2, calls);
  }

  [Fact]
  public async Task HeartbeatRunnerRethrowsTerminalErrors()
  {
    using var http = new HttpClient(new StubHandler(_ => new(HttpStatusCode.Unauthorized)));
    var exception = await Assert.ThrowsAsync<AersApiException>(() =>
      new AgentHeartbeatRunner(CreateClient(http)).RunAsync(Guid.Empty, TimeSpan.FromMilliseconds(1)));

    Assert.Equal(401, exception.StatusCode);
  }

  private static AersClient CreateClient(HttpClient http) => new(http, new()
  {
    BaseUrl = new("https://aers.test/"),
    ApiKey = "api"
  });

  private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
  {
    Content = new StringContent(content, Encoding.UTF8, "application/json")
  };

  private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) =>
      Task.FromResult(handler(request));
  }
}

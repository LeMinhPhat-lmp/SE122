using System.Text.Json;
using AERS.AgentClient;
using AERS.DummyAgent.Core.Behaviors;
using Xunit;

namespace AERS.UnitTests;

public sealed class DummyAgentCoreTests
{
  [Fact]
  public async Task AnalyzeCv_IsDeterministic()
  {
    var result = await RunAsync("analyze_cv", """{"cvText":"one two three"}""");

    Assert.Null(result.Error);
    Assert.Equal(91, result.Output!.Value.GetProperty("score").GetInt32());
    Assert.Equal(3, result.Output.Value.GetProperty("wordCount").GetInt32());
  }

  [Fact]
  public async Task Echo_ReturnsMessageAndDisplayName()
  {
    var result = await RunAsync("echo", """{"message":"hello"}""");

    Assert.Equal("hello", result.Output!.Value.GetProperty("message").GetString());
    Assert.Equal("Test Agent", result.Output.Value.GetProperty("handledBy").GetString());
  }

  [Fact]
  public async Task AlwaysFail_UsesReasonAndStableCode()
  {
    var result = await RunAsync("always_fail", """{"reason":"bad input"}""");

    Assert.Null(result.Output);
    Assert.Equal("DUMMY_FAILURE", result.Error!.Code);
    Assert.Equal("bad input", result.Error.Message);
  }

  [Fact]
  public async Task NeverReply_SuppressesTransportReply()
  {
    var result = await RunAsync("never_reply", "{}");

    Assert.True(result.SuppressReply);
    Assert.Null(result.Error);
  }

  [Fact]
  public async Task SlowEcho_HonorsCancellation()
  {
    using var cancellation = new CancellationTokenSource();
    var task = RunAsync("slow_echo", """{"message":"hello","delayMs":60000}""", cancellation.Token);
    cancellation.Cancel();

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
  }

  [Fact]
  public void CatalogSchemasRejectAdditionalProperties()
  {
    var schema = DummyCatalog.Get("echo").InputSchema;

    Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
    Assert.Equal("message", schema.GetProperty("required")[0].GetString());
  }

  private static async Task<InvocationResult> RunAsync(
    string capability,
    string input,
    CancellationToken cancellationToken = default)
  {
    var invocation = new TaskInvocation(
      Guid.NewGuid(), Guid.NewGuid(), null, 0, capability, Element(input),
      new PolicyContext(Guid.NewGuid(), Guid.NewGuid(), capability, "test", "test",
        Guid.NewGuid(), null, 0, null));
    var client = new AersClient(new HttpClient(new NoopHandler()), new()
    {
      BaseUrl = new("https://aers.test/")
    });
    await using var connection = new AersAgentConnection(new(new("https://aers.test/hubs/agent"), "api")
    {
      Transport = new NoopTransport()
    });
    return await DummyCatalog.Get(capability).Behavior(
      new DummyBehaviorContext(invocation, "Test Agent", Guid.NewGuid(), client, connection, TimeSpan.FromSeconds(1)),
      cancellationToken);
  }

  private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

  private sealed class NoopHandler : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
      Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
  }

  private sealed class NoopTransport : IHubTransport
  {
    public event Func<Exception?, Task>? Closed
    {
      add { }
      remove { }
    }

    public void OnReceive(Func<TaskEnvelope, Task> handler) { }
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<TaskAck> InvokeSendAsync(TaskEnvelope envelope, CancellationToken ct) =>
      Task.FromResult(new TaskAck(true, null, null, null));
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}

using System.Text;
using System.Text.Json;
using System.Net;
using AERS.AgentClient;
using Xunit;

namespace AERS.UnitTests;

public sealed class AersAgentConnectionTests
{
  [Fact]
  public async Task CallAsync_ReturnsResultDeliveredAfterAck()
  {
    var taskId = Guid.NewGuid();
    FakeHubTransport? transport = null;
    transport = new FakeHubTransport(async envelope =>
    {
      await Task.Yield();
      _ = transport!.ReceiveAsync(Result(taskId, TaskStatusValue.Completed, """{"score":42}"""));
      return new TaskAck(true, taskId, TaskStatusValue.Working, null);
    });
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();

    var outcome = await connection.CallAsync(Call(), TimeSpan.FromSeconds(1));

    Assert.Equal(taskId, outcome.TaskId);
    Assert.Equal(TaskStatusValue.Completed, outcome.Status);
    Assert.Equal(42, outcome.Output!.Value.GetProperty("score").GetInt32());
  }

  [Fact]
  public async Task CallAsync_BuffersResultDeliveredBeforeAck()
  {
    var taskId = Guid.NewGuid();
    FakeHubTransport? transport = null;
    transport = new FakeHubTransport(async envelope =>
    {
      await transport!.ReceiveAsync(Result(taskId, TaskStatusValue.Completed, """{"ok":true}"""));
      return new TaskAck(true, taskId, TaskStatusValue.Working, null);
    });
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();

    var outcome = await connection.CallAsync(Call(), TimeSpan.FromSeconds(1));

    Assert.True(outcome.Output!.Value.GetProperty("ok").GetBoolean());
  }

  [Fact]
  public async Task WaitForResultAsync_ReturnsResultThatArrivedBeforeWait()
  {
    var taskId = Guid.NewGuid();
    var transport = new FakeHubTransport(_ => Task.FromResult(new TaskAck(true, taskId, TaskStatusValue.Working, null)));
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();
    await transport.ReceiveAsync(Result(taskId, TaskStatusValue.Failed, null,
      new AersError("TARGET_UNAVAILABLE", "Target unavailable.")));

    var outcome = await connection.WaitForResultAsync(taskId, TimeSpan.FromSeconds(1));

    Assert.Equal(TaskStatusValue.Failed, outcome.Status);
    Assert.Equal("TARGET_UNAVAILABLE", outcome.Error!.Code);
  }

  [Fact]
  public async Task RejectedAck_ThrowsWithErrorAndDetails()
  {
    var details = JsonDocument.Parse("""{"errors":[{"path":"/payload/input","message":"Invalid."}]}""").RootElement.Clone();
    var transport = new FakeHubTransport(_ => Task.FromResult(new TaskAck(false, null, null,
      new AersError("VALIDATION_ERROR", "The request is invalid.", details))));
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();

    var exception = await Assert.ThrowsAsync<AersApiException>(() => connection.SubmitAsync(Call()));

    Assert.Equal(400, exception.StatusCode);
    Assert.Equal("VALIDATION_ERROR", exception.Code);
    Assert.Equal(details, exception.Details);
  }

  [Fact]
  public async Task FailedTask_IsReturnedAndTimeoutIsClientSide()
  {
    var taskId = Guid.NewGuid();
    var transport = new FakeHubTransport(_ => Task.FromResult(new TaskAck(true, taskId, TaskStatusValue.Failed, null)));
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();
    await transport.ReceiveAsync(Result(taskId, TaskStatusValue.Failed, null,
      new AersError("TARGET_UNAVAILABLE", "Target unavailable.")));

    var failed = await connection.CallAsync(Call(), TimeSpan.FromSeconds(1));
    Assert.Equal(TaskStatusValue.Failed, failed.Status);

    var missing = Guid.NewGuid();
    await Assert.ThrowsAsync<TaskResultTimeoutException>(() =>
      connection.WaitForResultAsync(missing, TimeSpan.FromMilliseconds(20)));
  }

  [Fact]
  public async Task InvocationHandlerRunsOffReceiveLoopAndCanMakeNestedCall()
  {
    var parentTaskId = Guid.NewGuid();
    var childTaskId = Guid.NewGuid();
    var sentResults = new TaskCompletionSource<TaskEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    FakeHubTransport? transport = null;
    transport = new FakeHubTransport(async envelope =>
    {
      if (envelope.MessageType == "task.create")
      {
        await transport!.ReceiveAsync(Result(childTaskId, TaskStatusValue.Completed, """{"child":true}"""));
        return new TaskAck(true, childTaskId, TaskStatusValue.Working, null);
      }

      sentResults.TrySetResult(envelope);
      return new TaskAck(true, parentTaskId, TaskStatusValue.Completed, null);
    });
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();
    connection.SetInvocationHandler(async (invocation, context, _) =>
    {
      var nested = await context.Connection.CallAsync(Call(), TimeSpan.FromSeconds(1));
      return new InvocationResult(nested.Output, null);
    });

    await transport.ReceiveAsync(Invocation(parentTaskId));
    var resultEnvelope = await sentResults.Task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal("task.result", resultEnvelope.MessageType);
    Assert.Equal(parentTaskId, resultEnvelope.Payload.GetProperty("taskId").GetGuid());
    Assert.True(resultEnvelope.Payload.GetProperty("output").GetProperty("child").GetBoolean());
  }

  [Fact]
  public async Task InvocationWithoutHandlerReturnsNoHandler()
  {
    var sent = new TaskCompletionSource<TaskEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    var taskId = Guid.NewGuid();
    var transport = new FakeHubTransport(envelope =>
    {
      sent.TrySetResult(envelope);
      return Task.FromResult(new TaskAck(true, taskId, TaskStatusValue.Failed, null));
    });
    await using var connection = CreateConnection(transport);
    await connection.ConnectAsync();

    await transport.ReceiveAsync(Invocation(taskId));
    var result = await sent.Task.WaitAsync(TimeSpan.FromSeconds(1));

    Assert.Equal("NO_HANDLER", result.Payload.GetProperty("error").GetProperty("code").GetString());
    Assert.Equal("failed", result.Payload.GetProperty("status").GetString());
  }

  [Fact]
  public async Task GetTaskAsync_SendsApiKeyHeader()
  {
    HttpRequestMessage? captured = null;
    using var http = new HttpClient(new StubHandler(request =>
    {
      captured = request;
      return new(HttpStatusCode.OK)
      {
        Content = new StringContent(
          $$"""{"taskId":"{{Guid.Empty}}","callerAgentId":"{{Guid.Empty}}","targetAgentId":"{{Guid.Empty}}","capability":"echo","status":"completed","output":{},"parentTaskId":null,"rootTaskId":"{{Guid.Empty}}","depth":0,"createdAt":"2026-01-01T00:00:00Z","startedAt":null,"completedAt":null,"error":null}""",
          Encoding.UTF8, "application/json")
      };
    }));
    var client = new AersClient(http, new() { BaseUrl = new("https://aers.test/"), ApiKey = "secret" });

    await client.GetTaskAsync(Guid.Empty);

    Assert.Equal("secret", captured!.Headers.GetValues("X-Api-Key").Single());
    Assert.DoesNotContain("secret", captured.RequestUri!.ToString());
  }

  private static AersAgentConnection CreateConnection(FakeHubTransport? transport) =>
    new(new(new("https://aers.test/hubs/agent"), "api") { Transport = transport });

  private static CreateTaskCall Call() => new(Guid.NewGuid(), "echo", Element("""{"message":"hi"}"""));

  private static TaskEnvelope Result(Guid taskId, TaskStatusValue status, string? output,
    AersError? error = null) =>
    new("task.result", JsonSerializer.SerializeToElement(new
    {
      taskId,
      status,
      output = output is null ? (JsonElement?)null : Element(output),
      error
    }, JsonSerializerOptions.Web));

  private static TaskEnvelope Invocation(Guid taskId) =>
    new("task.invoke", JsonSerializer.SerializeToElement(new
    {
      taskId,
      rootTaskId = taskId,
      parentTaskId = (Guid?)null,
      depth = 0,
      capability = "echo",
      input = Element("""{"message":"hi"}"""),
      policyContext = new
      {
        callerAgentId = Guid.NewGuid(),
        targetAgentId = Guid.NewGuid(),
        capability = "echo",
        callerDomain = "hr",
        targetDomain = "hr",
        rootTaskId = taskId,
        parentTaskId = (Guid?)null,
        depth = 0,
        ownerRef = (string?)null
      }
    }, JsonSerializerOptions.Web));

  private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

  private sealed class FakeHubTransport(
    Func<TaskEnvelope, Task<TaskAck>> send) : IHubTransport
  {
    private Func<TaskEnvelope, Task>? receive;

    public event Func<Exception?, Task>? Closed
    {
      add { }
      remove { }
    }

    public void OnReceive(Func<TaskEnvelope, Task> handler) => receive = handler;

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<TaskAck> InvokeSendAsync(TaskEnvelope envelope, CancellationToken ct) => send(envelope);

    public Task ReceiveAsync(TaskEnvelope envelope) =>
      receive?.Invoke(envelope) ?? Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    : HttpMessageHandler
  {
    protected override Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) =>
      Task.FromResult(handler(request));
  }
}

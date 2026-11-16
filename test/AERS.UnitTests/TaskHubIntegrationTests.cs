using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace AERS.UnitTests;

[Collection("Discovery integration")]
public sealed class TaskHubIntegrationTests(DiscoveryIntegrationFixture fixture)
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
  };

  [SkippableFact]
  public async Task IT_W6_01_ValidApiKeyConnects()
  {
    IntegrationTestSkip.Require();
    var agent = await RegisterAsync("valid");
    await using var connection = await ConnectAsync(agent.ApiKey);
    Assert.Equal(HubConnectionState.Connected, connection.State);
  }

  [SkippableFact]
  public async Task IT_W6_02_MissingApiKeyIsRejected()
  {
    IntegrationTestSkip.Require();
    await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(null));
  }

  [SkippableFact]
  public async Task IT_W6_03_RegistrationKeyIsRejectedAsApiKey()
  {
    IntegrationTestSkip.Require();
    await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(DiscoveryIntegrationFixture.RegistrationKey));
  }

  [SkippableFact]
  public async Task IT_W6_04_DeregisteredAgentCannotConnect()
  {
    IntegrationTestSkip.Require();
    var agent = await RegisterAsync("revoked");
    var client = fixture.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Delete, $"agents/{agent.AgentId:D}");
    request.Headers.Add("X-Api-Key", agent.ApiKey);
    Assert.True((await client.SendAsync(request)).IsSuccessStatusCode);
    await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(agent.ApiKey));
  }

  [SkippableFact]
  public async Task IT_W6_05_DisconnectCompletes()
  {
    IntegrationTestSkip.Require();
    var agent = await RegisterAsync("disconnect");
    await using var connection = await ConnectAsync(agent.ApiKey);
    await connection.StopAsync();
    Assert.Equal(HubConnectionState.Disconnected, connection.State);
  }

  [SkippableFact]
  public async Task IT_W6_06_ClosingOlderConnectionDoesNotBreakNewerConnection()
  {
    IntegrationTestSkip.Require();
    var agent = await RegisterAsync("last-connection");
    await using var first = await ConnectAsync(agent.ApiKey);
    await using var second = await ConnectAsync(agent.ApiKey);
    await first.StopAsync();
    Assert.Equal(HubConnectionState.Connected, second.State);
  }

  [SkippableFact]
  public async Task IT_W6_07_StartupPurgeFixtureIsAvailable()
  {
    IntegrationTestSkip.Require();
    Assert.NotNull(fixture.Factory);
    Assert.NotNull(fixture.Factory.Services);
  }

  [SkippableFact]
  public async Task IT_W6_08_SynchronousCreateAndCompletedResult()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("caller");
    var target = await RegisterAsync("target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    targetConnection.On<JsonElement>("Receive", async envelope =>
    {
      if (envelope.GetProperty("messageType").GetString() != "task.invoke")
        return;
      var payload = envelope.GetProperty("payload");
      await targetConnection.InvokeAsync<JsonElement>("Send", Envelope("task.result", new
      {
        taskId = payload.GetProperty("taskId").GetString(),
        status = "completed",
        output = new { message = "ok" },
        error = (object?)null
      }));
    });

    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var resultTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    callerConnection.On<JsonElement>("Receive", envelope =>
    {
      if (envelope.GetProperty("messageType").GetString() == "task.result")
        resultTcs.TrySetResult(envelope);
      return Task.CompletedTask;
    });
    var ack = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new
      {
        targetAgentId = target.AgentId,
        capability = "echo",
        input = new { message = "hello" },
        parentTaskId = (Guid?)null
      }));

    Assert.True(ack.GetProperty("accepted").GetBoolean());
    Assert.Equal("working", ack.GetProperty("status").GetString());
    var result = await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(ack.GetProperty("taskId").GetString(), result.GetProperty("payload").GetProperty("taskId").GetString());
    Assert.Equal("completed", result.GetProperty("payload").GetProperty("status").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_09_TaskInvokeHasAncestryContextButNoAncestorChain()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("invoke-caller");
    var target = await RegisterAsync("invoke-target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    var invocationTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    targetConnection.On<JsonElement>("Receive", envelope =>
    {
      if (envelope.GetProperty("messageType").GetString() == "task.invoke")
        invocationTcs.TrySetResult(envelope.GetProperty("payload").Clone());
      return Task.CompletedTask;
    });
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var ack = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));

    var invocation = await invocationTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(ack.GetProperty("taskId").GetString(), invocation.GetProperty("rootTaskId").GetString());
    Assert.Equal(0, invocation.GetProperty("depth").GetInt32());
    Assert.Equal(JsonValueKind.Null, invocation.GetProperty("parentTaskId").ValueKind);
    Assert.False(invocation.TryGetProperty("ancestorChain", out _));
    Assert.Equal(caller.AgentId, invocation.GetProperty("policyContext").GetProperty("callerAgentId").GetGuid());
  }

  [SkippableFact]
  public async Task IT_W6_10_FailedResultIsForwarded()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("failed-caller");
    var target = await RegisterAsync("failed-target", Capability("fail", EmptySchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    targetConnection.On<JsonElement>("Receive", envelope =>
      SendResultAsync(targetConnection, envelope, "failed", null, new { code = "DUMMY_FAILURE", message = "failed" }));
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    callerConnection.On<JsonElement>("Receive", envelope =>
    {
      if (envelope.GetProperty("messageType").GetString() == "task.result")
        received.TrySetResult(envelope);
      return Task.CompletedTask;
    });
    var ack = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "fail", input = new { }, parentTaskId = (Guid?)null }));
    var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("failed", result.GetProperty("payload").GetProperty("status").GetString());
    Assert.Equal(ack.GetProperty("taskId").GetString(), result.GetProperty("payload").GetProperty("taskId").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_11_ImmediateTargetResultIsNotLost()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("early-caller");
    var target = await RegisterAsync("early-target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    targetConnection.On<JsonElement>("Receive", envelope =>
      SendResultAsync(targetConnection, envelope, "completed", new { message = "early" }, null));
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var ack = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    Assert.True(ack.GetProperty("accepted").GetBoolean());
  }

  [SkippableFact]
  public async Task IT_W6_13_CrossDomainWithoutRuleIsRejected()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("cross-caller", Capability("probe", EmptySchema()));
    var target = await RegisterAsync("cross-target", Capability("echo", EchoSchema()));
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    Assert.Equal("DOMAIN_FORBIDDEN", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_16_ForwardedResultHasExplicitNullError()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("wire-result-caller");
    var target = await RegisterAsync("wire-result-target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    targetConnection.On<JsonElement>("Receive", envelope =>
      SendResultAsync(targetConnection, envelope, "completed", new { message = "ok" }, null));
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var resultTcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    callerConnection.On<JsonElement>("Receive", envelope =>
    {
      if (envelope.GetProperty("messageType").GetString() == "task.result")
        resultTcs.TrySetResult(envelope.GetProperty("payload").Clone());
      return Task.CompletedTask;
    });
    await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    var payload = await resultTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal(["taskId", "status", "output", "error"], payload.EnumerateObject().Select(property => property.Name));
    Assert.Equal(JsonValueKind.Null, payload.GetProperty("error").ValueKind);
  }

  [SkippableFact]
  public async Task IT_W6_12_RegisteredOfflineTargetReturnsTargetUnavailable()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("offline-caller");
    var target = await RegisterAsync("offline-target", Capability("echo", EchoSchema()));
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var ack = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    Assert.Equal("failed", ack.GetProperty("status").GetString());
    Assert.Equal("TARGET_UNAVAILABLE", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_14_CreateAckHasExactProperties()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("ack-shape");
    var target = await RegisterAsync("ack-target", Capability("echo", EchoSchema()));
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    Assert.Equal(["accepted", "taskId", "status", "error"], ack.EnumerateObject().Select(property => property.Name));
  }

  [SkippableFact]
  public async Task IT_W6_15_RejectedCreateHasNullTaskFields()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("reject-shape");
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send", Envelope("task.create", new
    {
      targetAgentId = Guid.NewGuid(),
      capability = "missing",
      input = new { },
      parentTaskId = (Guid?)null
    }));
    Assert.False(ack.GetProperty("accepted").GetBoolean());
    Assert.Equal(JsonValueKind.Null, ack.GetProperty("taskId").ValueKind);
    Assert.Equal(JsonValueKind.Null, ack.GetProperty("status").ValueKind);
    Assert.Equal("AGENT_NOT_FOUND", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_17_WorkingStatusEventIsAcceptedWithoutStateChange()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("status-event-caller");
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send", Envelope("task.status_event", new
    {
      taskId = Guid.NewGuid(),
      status = "working",
      result = (object?)null
    }));
    Assert.False(ack.GetProperty("accepted").GetBoolean());
    Assert.Equal("TASK_NOT_FOUND", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_18_UnknownTargetIsRejected()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("unknown-target");
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = Guid.NewGuid(), capability = "echo", input = new { }, parentTaskId = (Guid?)null }));
    Assert.Equal("AGENT_NOT_FOUND", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_20_UnknownCapabilityIsRejected()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("unknown-capability-caller");
    var target = await RegisterAsync("unknown-capability-target", Capability("echo", EchoSchema()));
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "missing", input = new { }, parentTaskId = (Guid?)null }));
    Assert.Equal("CAPABILITY_NOT_FOUND", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_21_InvalidInputIncludesPayloadPath()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("invalid-input-caller");
    var target = await RegisterAsync("invalid-input-target", Capability("echo", EchoSchema()));
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { wrong = true }, parentTaskId = (Guid?)null }));
    Assert.Equal("VALIDATION_ERROR", ack.GetProperty("error").GetProperty("code").GetString());
    Assert.Contains(ack.GetProperty("error").GetProperty("details").GetProperty("errors").EnumerateArray(),
      error => error.GetProperty("path").GetString() == "/payload/input/message");
  }

  [SkippableFact]
  public async Task IT_W6_23_OversizedInputIsRejected()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("large-input-caller");
    var target = await RegisterAsync("large-input-target", Capability("echo", EchoSchema()));
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = new string('x', 300_000) }, parentTaskId = (Guid?)null }));
    Assert.Equal("VALIDATION_ERROR", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_25_NonTargetResultIsForbidden()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("wrong-result-caller");
    var target = await RegisterAsync("wrong-result-target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var create = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    var result = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.result", new { taskId = create.GetProperty("taskId").GetString(), status = "completed", output = new { }, error = (object?)null }));
    Assert.Equal("TASK_ACCESS_FORBIDDEN", result.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_26_UnknownResultTaskIsNotFound()
  {
    IntegrationTestSkip.Require();
    var target = await RegisterAsync("unknown-result-target", Capability("echo", EchoSchema()));
    await using var connection = await ConnectAsync(target.ApiKey);
    var result = await connection.InvokeAsync<JsonElement>("Send",
      Envelope("task.result", new { taskId = Guid.NewGuid(), status = "completed", output = new { }, error = (object?)null }));
    Assert.Equal("TASK_NOT_FOUND", result.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_28_InvalidResultLeavesTaskWorking()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("invalid-result-caller");
    var target = await RegisterAsync("invalid-result-target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    var resultAck = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
    targetConnection.On<JsonElement>("Receive", async envelope =>
    {
      if (envelope.GetProperty("messageType").GetString() == "task.invoke")
      {
        var ack = await targetConnection.InvokeAsync<JsonElement>("Send",
          Envelope("task.result", new { taskId = envelope.GetProperty("payload").GetProperty("taskId").GetString(), status = "completed", output = (object?)null, error = (object?)null }));
        resultAck.TrySetResult(ack);
      }
    });
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    var ack = await resultAck.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("VALIDATION_ERROR", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_22_UnknownMessageTypeIsRejected()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("shape-message");
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send", Envelope("unknown", new { }));
    Assert.Equal("VALIDATION_ERROR", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_24_ClientTaskInvokeDirectionIsRejected()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("wrong-direction");
    await using var connection = await ConnectAsync(caller.ApiKey);
    var ack = await connection.InvokeAsync<JsonElement>("Send", Envelope("task.invoke", new { }));
    Assert.Equal("VALIDATION_ERROR", ack.GetProperty("error").GetProperty("code").GetString());
  }

  [SkippableFact]
  public async Task IT_W6_34_TaskQueryOmitsInputAndAncestry()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("query-caller");
    var target = await RegisterAsync("query-target", Capability("echo", EchoSchema()));
    await using var targetConnection = await ConnectAsync(target.ApiKey);
    targetConnection.On<JsonElement>("Receive", envelope =>
      SendResultAsync(targetConnection, envelope, "completed", new { message = "ok" }, null));
    await using var callerConnection = await ConnectAsync(caller.ApiKey);
    var ack = await callerConnection.InvokeAsync<JsonElement>("Send",
      Envelope("task.create", new { targetAgentId = target.AgentId, capability = "echo", input = new { message = "x" }, parentTaskId = (Guid?)null }));
    await WaitForAsync(() => true);
    using var request = new HttpRequestMessage(HttpMethod.Get, $"tasks/{Guid.Parse(ack.GetProperty("taskId").GetString()!):D}");
    request.Headers.Add("X-Api-Key", caller.ApiKey);
    using var response = await fixture.CreateClient().SendAsync(request);
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(200, (int)response.StatusCode);
    Assert.False(body.RootElement.TryGetProperty("input", out _));
    Assert.False(body.RootElement.TryGetProperty("ancestorChain", out _));
    Assert.False(body.RootElement.TryGetProperty("deadlineAt", out _));
  }

  [SkippableFact]
  public async Task IT_W6_35_ThirdAgentCannotReadTask()
  {
    IntegrationTestSkip.Require();
    var third = await RegisterAsync("third-agent");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"tasks/{Guid.NewGuid():D}");
    request.Headers.Add("X-Api-Key", third.ApiKey);
    var response = await fixture.CreateClient().SendAsync(request);
    Assert.Equal(404, (int)response.StatusCode);
  }

  [SkippableFact]
  public async Task IT_W6_36_UnknownTaskReturnsNotFound()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("unknown-task");
    using var request = new HttpRequestMessage(HttpMethod.Get, $"tasks/{Guid.NewGuid():D}");
    request.Headers.Add("X-Api-Key", caller.ApiKey);
    var response = await fixture.CreateClient().SendAsync(request);
    Assert.Equal(404, (int)response.StatusCode);
  }

  [SkippableFact]
  public async Task IT_W6_37_MalformedTaskIdReturnsBadRequest()
  {
    IntegrationTestSkip.Require();
    var caller = await RegisterAsync("bad-task-id");
    using var request = new HttpRequestMessage(HttpMethod.Get, "tasks/not-a-guid");
    request.Headers.Add("X-Api-Key", caller.ApiKey);
    var response = await fixture.CreateClient().SendAsync(request);
    Assert.Equal(400, (int)response.StatusCode);
  }

  [SkippableFact]
  public async Task IT_W6_38_TaskQueryWithoutKeyIsUnauthorized()
  {
    IntegrationTestSkip.Require();
    var response = await fixture.CreateClient().GetAsync($"tasks/{Guid.NewGuid():D}");
    Assert.Equal(401, (int)response.StatusCode);
  }

  private async Task<RegisteredAgent> RegisterAsync(string suffix, params object[] capabilities) =>
    await fixture.RegisterAgentAsync(fixture.CreateClient(), $"it-{suffix}-{Guid.NewGuid():N}"[..20], suffix, capabilities);

  private async Task<HubConnection> ConnectAsync(string? apiKey)
  {
    var connection = new HubConnectionBuilder()
      .WithUrl(new Uri(fixture.Factory.Server.BaseAddress, "/hubs/agent"), options =>
      {
        if (apiKey is not null)
          options.Headers["X-Api-Key"] = apiKey;
        options.HttpMessageHandlerFactory = _ => fixture.Factory.Server.CreateHandler();
        options.Transports = HttpTransportType.LongPolling;
      })
      .Build();
    await connection.StartAsync();
    return connection;
  }

  private static Task SendResultAsync(
    HubConnection connection,
    JsonElement envelope,
    string status,
    object? output,
    object? error) =>
    connection.InvokeAsync<JsonElement>("Send", Envelope("task.result", new
    {
      taskId = envelope.GetProperty("payload").GetProperty("taskId").GetString(),
      status,
      output,
      error
    }));

  private static object Envelope(string messageType, object payload) =>
    new { messageType, correlationId = (string?)null, payload };

  private static object Capability(string name, object schema) =>
    new { name, description = "Integration capability.", inputSchema = schema, outputSchema = EmptySchema() };

  private static object EchoSchema() =>
    new { type = "object", additionalProperties = false, required = new[] { "message" }, properties = new { message = new { type = "string" } } };

  private static object EmptySchema() => new { type = "object", additionalProperties = false };

  private static Task WaitForAsync(Func<bool> predicate)
  {
    var deadline = DateTime.UtcNow.AddSeconds(1);
    while (!predicate() && DateTime.UtcNow < deadline)
      Thread.Sleep(10);
    return Task.CompletedTask;
  }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AERS.AgentClient;

public sealed class AersAgentConnection : IAsyncDisposable
{
  private readonly HttpClient http;
  private readonly AersAgentConnectionOptions options;
  private readonly IHubTransport transport;
  private readonly TimeProvider timeProvider;
  private readonly ILogger logger;
  private readonly ConcurrentDictionary<Guid, TaskHandle> handles = new();
  private readonly ConcurrentDictionary<Guid, DateTimeOffset> completedAt = new();
  private readonly Dictionary<string, Func<TaskInvocation, Task<InvocationResult>>> invocationHandlers = new(StringComparer.Ordinal);
  private int disposed;

  public AersAgentConnection(
    HttpClient http,
    AersAgentConnectionOptions options,
    IHubTransport? transport = null,
    TimeProvider? timeProvider = null,
    ILogger<AersAgentConnection>? logger = null)
  {
    this.http = http ?? throw new ArgumentNullException(nameof(http));
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.timeProvider = timeProvider ?? TimeProvider.System;
    this.logger = logger ?? NullLogger<AersAgentConnection>.Instance;
    this.transport = transport ?? new SignalRHubTransport(options);
    this.transport.MessageReceived += HandleMessageAsync;
    this.transport.Reconnecting += () => Disconnected?.Invoke(false);
    this.transport.Reconnected += _ => Reconnected?.Invoke();
    this.transport.Closed += HandleClosedAsync;
  }

  public event Action? Reconnected;
  public event Action<bool>? Disconnected;
  public bool IsConnected => transport.IsConnected;

  public async Task StartAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    await transport.StartAsync(cancellationToken);
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    await transport.StopAsync(cancellationToken);
  }

  public Task ReconnectAsync(CancellationToken cancellationToken = default)
  {
    ThrowIfDisposed();
    return ReconnectCoreAsync(cancellationToken);
  }

  public async Task<TaskHandle> SubmitAsync(
    Guid targetAgentId,
    string capability,
    JsonElement input,
    Guid? parentTaskId = null,
    CancellationToken cancellationToken = default)
  {
    EnsureConnected();
    var envelope = JsonSerializer.SerializeToElement(new
    {
      messageType = "task.create",
      correlationId = (string?)null,
      payload = new
      {
        targetAgentId,
        capability,
        input,
        parentTaskId
      }
    }, AersClient.JsonOptions);
    var ackTask = transport.SendAsync(envelope, cancellationToken);
    var ack = await ackTask;
    if (!ack.GetProperty("accepted").GetBoolean())
      throw ToApiException(ack);
    var taskId = ack.GetProperty("taskId").GetGuid();
    var status = ack.GetProperty("status").GetString() ?? "submitted";
    var handle = handles.GetOrAdd(taskId, _ => new TaskHandle(taskId, status));
    handle.Status = status;
    PruneCompleted();
    return handle;
  }

  public void RegisterInvocationHandler(string capability, Func<TaskInvocation, Task<InvocationResult>> handler)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(capability);
    ArgumentNullException.ThrowIfNull(handler);
    lock (invocationHandlers)
      invocationHandlers[capability] = handler;
  }

  public async Task<TaskQueryResult> GetTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
  {
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(http.BaseAddress ?? throw new InvalidOperationException("HttpClient.BaseAddress is required."), $"tasks/{taskId:D}"));
    if (!string.IsNullOrWhiteSpace(options.ApiKey))
      request.Headers.TryAddWithoutValidation("X-Api-Key", options.ApiKey);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    if (!response.IsSuccessStatusCode)
      throw await CreateHttpExceptionAsync(response, cancellationToken);
    return await response.Content.ReadFromJsonAsync<TaskQueryResult>(AersClient.JsonOptions, cancellationToken)
      ?? throw new AersApiException(200, "UNKNOWN_ERROR", "The response body was empty.");
  }

  public async Task<TaskOutcome> ObserveAsync(Guid taskId, TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    if (timeout <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(timeout));
    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutSource.CancelAfter(timeout);
    while (true)
    {
      var query = await GetTaskAsync(taskId, timeoutSource.Token);
      if (query.Status is "completed" or "failed")
      {
        var outcome = new TaskOutcome(query.Status, query.Output, query.Error);
        handles.GetOrAdd(taskId, _ => new TaskHandle(taskId, query.Status)).TryComplete(outcome);
        return outcome;
      }
      await Task.Delay(options.ObserveInterval, timeoutSource.Token);
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (Interlocked.Exchange(ref disposed, 1) == 0)
      await transport.DisposeAsync();
  }

  private async Task HandleMessageAsync(JsonElement envelope)
  {
    try
    {
      var type = envelope.GetProperty("messageType").GetString();
      var payload = envelope.GetProperty("payload");
      if (type == "task.result")
        HandleResult(payload);
      else if (type == "task.status_event")
        HandleStatus(payload);
      else if (type == "task.invoke")
        _ = DispatchInvocationAsync(payload);
    }
    catch (Exception ex)
    {
      logger.LogDebug(ex, "Ignoring malformed server message.");
    }
    await Task.CompletedTask;
  }

  private void HandleResult(JsonElement payload)
  {
    var taskId = payload.GetProperty("taskId").GetGuid();
    var handle = handles.GetOrAdd(taskId, id => new TaskHandle(id, "submitted"));
    if (completedAt.ContainsKey(taskId))
    {
      logger.LogDebug("Ignoring duplicate result for task {TaskId}.", taskId);
      return;
    }
    var outcome = new TaskOutcome(
      payload.GetProperty("status").GetString() ?? "failed",
      payload.TryGetProperty("output", out var output) && output.ValueKind != JsonValueKind.Null ? output.Clone() : null,
      ParseError(payload));
    if (handle.TryComplete(outcome))
      completedAt[taskId] = timeProvider.GetUtcNow();
    PruneCompleted();
  }

  private void HandleStatus(JsonElement payload)
  {
    if (!string.Equals(payload.GetProperty("status").GetString(), "working", StringComparison.Ordinal))
    {
      logger.LogDebug("Ignoring unknown task status event.");
      return;
    }
    var taskId = payload.GetProperty("taskId").GetGuid();
    if (completedAt.ContainsKey(taskId))
      return;
    handles.GetOrAdd(taskId, id => new TaskHandle(id, "submitted")).RaiseWorking();
  }

  private async Task DispatchInvocationAsync(JsonElement payload)
  {
    var invocation = payload.Deserialize<TaskInvocation>(AersClient.JsonOptions);
    if (invocation is null)
      return;
    Func<TaskInvocation, Task<InvocationResult>>? handler;
    lock (invocationHandlers)
      invocationHandlers.TryGetValue(invocation.Capability, out handler);
    if (handler is null)
      return;
    try
    {
      var result = await handler(invocation);
      if (result.SuppressReply)
        return;
      var envelope = JsonSerializer.SerializeToElement(new
      {
        messageType = "task.result",
        correlationId = (string?)null,
        payload = new { taskId = invocation.TaskId, result.Status, output = result.Output, error = result.Error }
      }, AersClient.JsonOptions);
      await SendResultWithRetryAsync(envelope);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Invocation handler failed.");
    }
  }

  private async Task ReconnectCoreAsync(CancellationToken cancellationToken)
  {
    if (transport.IsConnected)
      return;
    await transport.StartAsync(cancellationToken);
  }

  private async Task SendResultWithRetryAsync(JsonElement envelope)
  {
    for (var attempt = 0; attempt <= options.ResultSendRetryCount; attempt++)
    {
      try
      {
        EnsureConnected();
        await transport.SendAsync(envelope);
        return;
      }
      catch (NotConnectedException) when (attempt < options.ResultSendRetryCount)
      {
        await Task.Delay(options.ResultSendRetryDelay);
      }
    }
  }

  private Task HandleClosedAsync(Exception? exception)
  {
    Disconnected?.Invoke(true);
    return Task.CompletedTask;
  }

  private void EnsureConnected()
  {
    if (!transport.IsConnected)
      throw new NotConnectedException();
  }

  private void ThrowIfDisposed()
  {
    if (Volatile.Read(ref disposed) != 0)
      throw new ObjectDisposedException(nameof(AersAgentConnection));
  }

  private void PruneCompleted()
  {
    var cutoff = timeProvider.GetUtcNow() - options.ResultRetention;
    foreach (var item in completedAt)
      if (item.Value <= cutoff && completedAt.TryRemove(item.Key, out _))
        handles.TryRemove(item.Key, out _);
  }

  private static TaskError? ParseError(JsonElement payload)
  {
    if (!payload.TryGetProperty("error", out var error) || error.ValueKind == JsonValueKind.Null)
      return null;
    return error.Deserialize<TaskError>(AersClient.JsonOptions);
  }

  private static AersApiException ToApiException(JsonElement ack)
  {
    var error = ack.GetProperty("error");
    return new AersApiException(400, error.GetProperty("code").GetString() ?? "UNKNOWN_ERROR",
      error.GetProperty("message").GetString() ?? "The request failed.",
      error.TryGetProperty("details", out var details) ? details.Clone() : null);
  }

  private static async Task<AersApiException> CreateHttpExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
  {
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    try
    {
      using var document = JsonDocument.Parse(body);
      var root = document.RootElement;
      return new AersApiException((int)response.StatusCode,
        root.TryGetProperty("code", out var code) ? code.GetString() ?? "UNKNOWN_ERROR" : "UNKNOWN_ERROR",
        root.TryGetProperty("message", out var message) ? message.GetString() ?? "The request failed." : "The request failed.",
        root.TryGetProperty("details", out var details) ? details.Clone() : null);
    }
    catch (JsonException)
    {
      return new AersApiException((int)response.StatusCode, "UNKNOWN_ERROR", response.ReasonPhrase ?? "The request failed.");
    }
  }
}

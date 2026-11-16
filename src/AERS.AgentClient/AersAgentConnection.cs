using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Connections.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AERS.AgentClient;

public enum ConnectionState
{
  Disconnected,
  Connecting,
  Connected,
  Closed
}

public sealed record AersAgentConnectionOptions(
  Uri HubUri,
  string ApiKey)
{
  public int MaxConcurrentInvocations { get; init; } = int.MaxValue;
  public int ResultBufferCapacity { get; init; } = 1000;
  public TimeSpan ResultBufferRetention { get; init; } = TimeSpan.FromMinutes(5);
  public Action<HttpConnectionOptions>? ConfigureHttpConnection { get; init; }
  public IHubTransport? Transport { get; init; }
}

public delegate Task<InvocationResult> InvocationHandler(
  TaskInvocation invocation,
  InvocationContext context,
  CancellationToken ct);

public interface IHubTransport : IAsyncDisposable
{
  event Func<Exception?, Task>? Closed;
  void OnReceive(Func<TaskEnvelope, Task> handler);
  Task StartAsync(CancellationToken ct);
  Task StopAsync(CancellationToken ct);
  Task<TaskAck> InvokeSendAsync(TaskEnvelope envelope, CancellationToken ct);
}

public sealed class TaskResultTimeoutException(Guid taskId, TimeSpan timeout)
  : TimeoutException($"Task result '{taskId}' was not received within {timeout}.")
{
  public Guid TaskId { get; } = taskId;
  public TimeSpan Timeout { get; } = timeout;
}

public sealed class ConnectionClosedException(Exception? cause = null)
  : Exception("The agent connection was closed.", cause);

public sealed class AersAgentConnection : IAsyncDisposable
{
  private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
  private readonly AersAgentConnectionOptions options;
  private readonly ILogger<AersAgentConnection>? logger;
  private readonly ConcurrentDictionary<Guid, PendingResult> pendingResults = new();
  private readonly SemaphoreSlim invocationGate;
  private readonly object lifecycleLock = new();
  private IHubTransport? transport;
  private InvocationHandler? invocationHandler;
  private ConnectionState state;
  private bool disposed;

  public AersAgentConnection(AersAgentConnectionOptions options, ILogger<AersAgentConnection>? logger = null)
  {
    ArgumentNullException.ThrowIfNull(options);
    if (!options.HubUri.IsAbsoluteUri)
      throw new ArgumentException("HubUri must be an absolute URI.", nameof(options));
    if (string.IsNullOrWhiteSpace(options.ApiKey))
      throw new ArgumentException("ApiKey is required.", nameof(options));
    if (options.MaxConcurrentInvocations < 1)
      throw new ArgumentOutOfRangeException(nameof(options), "MaxConcurrentInvocations must be positive.");
    if (options.ResultBufferCapacity < 1)
      throw new ArgumentOutOfRangeException(nameof(options), "ResultBufferCapacity must be positive.");
    if (options.ResultBufferRetention <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(options), "ResultBufferRetention must be positive.");

    this.options = options;
    this.logger = logger;
    invocationGate = new SemaphoreSlim(options.MaxConcurrentInvocations);
  }

  public ConnectionState State
  {
    get
    {
      lock (lifecycleLock)
        return state;
    }
  }

  public event Func<Exception?, Task>? Closed;

  public async Task ConnectAsync(CancellationToken ct = default)
  {
    IHubTransport selectedTransport;
    lock (lifecycleLock)
    {
      if (disposed)
        throw new ObjectDisposedException(nameof(AersAgentConnection));
      if (state == ConnectionState.Connected)
        return;
      if (state == ConnectionState.Connecting)
        throw new InvalidOperationException("The connection is already connecting.");
      state = ConnectionState.Connecting;
      selectedTransport = transport ??= options.Transport ?? CreateTransport();
      selectedTransport.OnReceive(HandleReceiveAsync);
      selectedTransport.Closed += HandleClosedAsync;
    }

    try
    {
      await selectedTransport.StartAsync(ct);
      lock (lifecycleLock)
        state = ConnectionState.Connected;
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
    {
      lock (lifecycleLock)
        state = ConnectionState.Closed;
      throw new AersApiException(401, "AUTHENTICATION_FAILED", "Authentication failed.");
    }
    catch
    {
      lock (lifecycleLock)
        state = ConnectionState.Closed;
      throw;
    }
  }

  public async Task DisconnectAsync(CancellationToken ct = default)
  {
    IHubTransport? selectedTransport;
    lock (lifecycleLock)
    {
      if (disposed)
        return;
      selectedTransport = transport;
      state = ConnectionState.Disconnected;
    }

    if (selectedTransport is not null)
      await selectedTransport.StopAsync(ct);
  }

  public async Task<TaskAck> SubmitAsync(CreateTaskCall call, CancellationToken ct = default)
  {
    EnsureConnected();
    CleanupBufferedResults();
    var payload = JsonSerializer.SerializeToElement(new
    {
      targetAgentId = call.TargetAgentId,
      capability = call.Capability,
      input = call.Input,
      parentTaskId = call.ParentTaskId
    }, JsonOptions);
    var ack = await transport!.InvokeSendAsync(new TaskEnvelope("task.create", payload), ct);
    if (!ack.Accepted)
      throw CreateRejectedAckException(ack);
    return ack;
  }

  public async Task<TaskOutcome> WaitForResultAsync(
    Guid taskId,
    TimeSpan timeout,
    CancellationToken ct = default)
  {
    if (timeout <= TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(timeout));
    EnsureConnected();
    CleanupBufferedResults();
    var pending = pendingResults.GetOrAdd(taskId, _ => new PendingResult());
    try
    {
      var outcome = await pending.Completion.Task.WaitAsync(timeout, ct);
      pendingResults.TryRemove(new KeyValuePair<Guid, PendingResult>(taskId, pending));
      return outcome;
    }
    catch (TimeoutException)
    {
      pendingResults.TryRemove(new KeyValuePair<Guid, PendingResult>(taskId, pending));
      throw new TaskResultTimeoutException(taskId, timeout);
    }
  }

  public async Task<TaskOutcome> CallAsync(
    CreateTaskCall call,
    TimeSpan timeout,
    CancellationToken ct = default)
  {
    var ack = await SubmitAsync(call, ct);
    if (ack.TaskId is null)
      throw new InvalidOperationException("An accepted task acknowledgement must include a taskId.");
    return await WaitForResultAsync(ack.TaskId.Value, timeout, ct);
  }

  public void SetInvocationHandler(InvocationHandler handler) =>
    invocationHandler = handler ?? throw new ArgumentNullException(nameof(handler));

  public async ValueTask DisposeAsync()
  {
    IHubTransport? selectedTransport;
    lock (lifecycleLock)
    {
      if (disposed)
        return;
      disposed = true;
      state = ConnectionState.Closed;
      selectedTransport = transport;
    }

    foreach (var pending in pendingResults.Values)
      pending.Completion.TrySetCanceled();
    pendingResults.Clear();
    if (selectedTransport is not null)
    {
      await selectedTransport.StopAsync(CancellationToken.None);
      await selectedTransport.DisposeAsync();
    }
    invocationGate.Dispose();
  }

  private async Task HandleReceiveAsync(TaskEnvelope envelope)
  {
    switch (envelope.MessageType)
    {
      case "task.result":
        HandleResult(envelope);
        break;
      case "task.invoke":
        _ = Task.Run(() => ProcessInvocationAsync(envelope));
        break;
      default:
        logger?.LogDebug("Ignoring unknown task message type {MessageType}.", envelope.MessageType);
        break;
    }

    await Task.CompletedTask;
  }

  private void HandleResult(TaskEnvelope envelope)
  {
    try
    {
      var outcome = JsonSerializer.Deserialize<TaskOutcome>(envelope.Payload, JsonOptions)
        ?? throw new JsonException("The task result payload was null.");
      var pending = pendingResults.GetOrAdd(outcome.TaskId, _ => new PendingResult());
      pending.BufferedAt = DateTimeOffset.UtcNow;
      pending.Completion.TrySetResult(outcome);
      CleanupBufferedResults();
    }
    catch (JsonException ex)
    {
      logger?.LogWarning(ex, "Ignoring an invalid task result payload.");
    }
  }

  private async Task ProcessInvocationAsync(TaskEnvelope envelope)
  {
    await invocationGate.WaitAsync();
    try
    {
      TaskInvocation invocation;
      try
      {
        invocation = JsonSerializer.Deserialize<TaskInvocation>(envelope.Payload, JsonOptions)
          ?? throw new JsonException("The task invocation payload was null.");
      }
      catch (JsonException ex)
      {
        logger?.LogWarning(ex, "Ignoring an invalid task invocation payload.");
        return;
      }

      InvocationResult result;
      var handler = invocationHandler;
      if (handler is null)
      {
        result = new InvocationResult(null, new AersError("NO_HANDLER", "No invocation handler is registered."));
      }
      else
      {
        try
        {
          result = await handler(invocation, new InvocationContext(this), CancellationToken.None);
        }
        catch (Exception ex)
        {
          logger?.LogError(ex, "Task invocation handler failed.");
          result = new InvocationResult(null,
            new AersError("AGENT_HANDLER_ERROR", $"{ex.GetType().Name}: {ex.Message}"));
        }
      }

      var payload = JsonSerializer.SerializeToElement(new
      {
        taskId = invocation.TaskId,
        status = result.Error is null ? TaskStatusValue.Completed : TaskStatusValue.Failed,
        output = result.Error is null ? result.Output : null,
        error = result.Error
      }, JsonOptions);
      var ack = await transport!.InvokeSendAsync(new TaskEnvelope("task.result", payload), CancellationToken.None);
      if (!ack.Accepted)
        logger?.LogWarning("The task result was rejected with {ErrorCode}.", ack.Error?.Code);
    }
    catch (Exception ex)
    {
      logger?.LogError(ex, "Failed to process a task invocation.");
    }
    finally
    {
      invocationGate.Release();
    }
  }

  private async Task HandleClosedAsync(Exception? exception)
  {
    lock (lifecycleLock)
      state = ConnectionState.Closed;
    var closedException = new ConnectionClosedException(exception);
    foreach (var pending in pendingResults.Values)
      pending.Completion.TrySetException(closedException);
    pendingResults.Clear();
    var handler = Closed;
    if (handler is not null)
      await handler(exception);
  }

  private IHubTransport CreateTransport() => new SignalRHubTransport(options);

  private void EnsureConnected()
  {
    lock (lifecycleLock)
    {
      if (disposed)
        throw new ObjectDisposedException(nameof(AersAgentConnection));
      if (state != ConnectionState.Connected || transport is null)
        throw new InvalidOperationException("The agent connection is not connected.");
    }
  }

  private void CleanupBufferedResults()
  {
    var cutoff = DateTimeOffset.UtcNow - options.ResultBufferRetention;
    foreach (var entry in pendingResults)
    {
      if (entry.Value.Completion.Task.IsCompleted &&
          (entry.Value.BufferedAt is null || entry.Value.BufferedAt < cutoff))
        pendingResults.TryRemove(new KeyValuePair<Guid, PendingResult>(entry.Key, entry.Value));
    }

    var buffered = pendingResults
      .Where(entry => entry.Value.Completion.Task.IsCompleted)
      .OrderBy(entry => entry.Value.BufferedAt)
      .ToList();
    foreach (var entry in buffered.Take(Math.Max(0, buffered.Count - options.ResultBufferCapacity)))
    {
      if (pendingResults.TryRemove(new KeyValuePair<Guid, PendingResult>(entry.Key, entry.Value)))
        logger?.LogWarning("Evicted buffered task result {TaskId}.", entry.Key);
    }
  }

  private static AersApiException CreateRejectedAckException(TaskAck ack)
  {
    var error = ack.Error ?? new AersError("UNKNOWN_ERROR", "The task request was rejected.");
    var statusCode = error.Code switch
    {
      "VALIDATION_ERROR" => 400,
      "AUTHENTICATION_FAILED" => 401,
      "DOMAIN_FORBIDDEN" or "TASK_ACCESS_FORBIDDEN" => 403,
      "AGENT_NOT_FOUND" or "TASK_NOT_FOUND" or "CAPABILITY_NOT_FOUND" => 404,
      "TASK_STATE_CONFLICT" => 409,
      _ => 0
    };
    return new AersApiException(statusCode, error.Code, error.Message, error.Details);
  }

  private static JsonSerializerOptions CreateJsonOptions()
  {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
      DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
    options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    return options;
  }

  private sealed class PendingResult
  {
    public TaskCompletionSource<TaskOutcome> Completion { get; } =
      new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DateTimeOffset? BufferedAt { get; set; }
  }
}

internal sealed class SignalRHubTransport : IHubTransport
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
  private readonly HubConnection connection;

  public SignalRHubTransport(AersAgentConnectionOptions options)
  {
    connection = new HubConnectionBuilder()
      .WithUrl(options.HubUri, httpOptions =>
      {
        httpOptions.Headers["X-Api-Key"] = options.ApiKey;
        options.ConfigureHttpConnection?.Invoke(httpOptions);
      })
      .AddJsonProtocol(json => json.PayloadSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
      .Build();
  }

  public event Func<Exception?, Task>? Closed
  {
    add => connection.Closed += value;
    remove => connection.Closed -= value;
  }

  public void OnReceive(Func<TaskEnvelope, Task> handler) => connection.On("Receive", handler);

  public Task StartAsync(CancellationToken ct) => connection.StartAsync(ct);

  public Task StopAsync(CancellationToken ct) => connection.StopAsync();

  public Task<TaskAck> InvokeSendAsync(TaskEnvelope envelope, CancellationToken ct) =>
    connection.InvokeAsync<TaskAck>("Send", envelope, ct);

  public ValueTask DisposeAsync() => connection.DisposeAsync();
}

using System.Text.Json;
using Application.Constants;
using Application.DTOs.Tasks;
using Application.Interfaces.Realtime;
using Application.Interfaces.Runtime;
using Application.Tasks;
using Microsoft.Extensions.Logging;

namespace Application.Services;

/// <summary>
/// The one component that pushes task.result and task.status_event to a caller (A41, ADR-W7-02). It never throws except on
/// cancellation. A result that cannot be pushed is not recorded anywhere: the task stays unacknowledged in PostgreSQL, so it is
/// redelivered when the caller reconnects and can always be read with GET /tasks/{id}. A working event is never replayed.
/// </summary>
public sealed class TaskNotifier(
  IConnectionRegistry connectionRegistry,
  IMessageRelay messageRelay,
  ILogger<TaskNotifier> logger) : ITaskNotifier
{
  public Task NotifyResultAsync(Guid callerAgentId, TaskResult result, CancellationToken cancellationToken = default) =>
    PushAsync(
      callerAgentId,
      result.TaskId,
      new TaskEnvelope(TaskMessageTypes.Result, null, JsonSerializer.SerializeToElement(result, EnvelopeJson.Options)),
      cancellationToken);

  public Task NotifyWorkingAsync(Guid callerAgentId, Guid taskId, CancellationToken cancellationToken = default) =>
    PushAsync(
      callerAgentId,
      taskId.ToString(),
      new TaskEnvelope(
        TaskMessageTypes.StatusEvent,
        null,
        JsonSerializer.SerializeToElement(new TaskStatusEventPush(taskId.ToString(), "working", null), EnvelopeJson.Options)),
      cancellationToken);

  private async Task PushAsync(Guid agentId, string taskId, TaskEnvelope envelope, CancellationToken cancellationToken)
  {
    try
    {
      var connection = await connectionRegistry.ResolveAsync(agentId, cancellationToken);
      if (connection is null)
      {
        logger.LogInformation("Agent {AgentId} has no connection; the {MessageType} of task {TaskId} is not pushed.", agentId, envelope.MessageType, taskId);
        return;
      }

      await messageRelay.SendAsync(connection, envelope, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not push the {MessageType} of task {TaskId} to agent {AgentId}.", envelope.MessageType, taskId, agentId);
    }
  }
}

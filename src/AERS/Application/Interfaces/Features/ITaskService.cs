using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;
using Application.Models;
using Domain.Entities;

namespace Application.Interfaces.Features;

/// <summary>Task creation, dispatch, result handling and query (frozen contract, decision A40).</summary>
public interface ITaskService
{
  /// <summary>Validates a task.create request and persists a submitted Task with its ancestry. A rejection never creates a Task (A20).</summary>
  Task<Result<AgentTask>> CreateAsync(CreateTaskRequest request, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);

  /// <summary>Moves a submitted Task to working (committed before sending) and relays task.invoke to the target (A27).</summary>
  Task<Result<TaskDispatchOutcome>> DispatchAsync(AgentTask task, CancellationToken cancellationToken = default);

  /// <summary>Handles task.result from the target agent (A28, A39).</summary>
  Task<Result<TaskResultOutcome>> ReceiveResultAsync(TaskResult result, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default);

  /// <summary>Handles task.status_event from the target agent. Only "working" is accepted and it changes nothing (A23).</summary>
  Task<Result<TaskStatusEventOutcome>> ReceiveStatusEventAsync(TaskStatusEvent statusEvent, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default);

  /// <summary>GET /tasks/{taskId}: PostgreSQL only; 404 before 403; only the caller or the target may read (A33).</summary>
  Task<Result<TaskQueryResult>> GetAsync(Guid taskId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);

  /// <summary>
  /// Handles task.ack from the caller (ADR-W7-02): sets acknowledged_at with one conditional UPDATE. Idempotent: a repeated ack is accepted.
  /// TASK_NOT_FOUND, TASK_ACCESS_FORBIDDEN (sender is not the caller) and TASK_STATE_CONFLICT (task not terminal) are the only failures.
  /// </summary>
  Task<Result<TaskAckOutcome>> AcknowledgeAsync(TaskAck ack, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default);

  /// <summary>
  /// Fails a submitted or working task whose deadline has passed with TASK_TIMEOUT (A46). Idempotent. Pushes nothing: the caller
  /// of this method pushes ResultToPush after the commit.
  /// </summary>
  Task<Result<TaskExpiryOutcome>> ExpireAsync(Guid taskId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Fails up to <paramref name="limit"/> working tasks of a target agent that did not reconnect within the grace period, with
  /// TARGET_UNAVAILABLE (ADR-W7-03). Each task is a compare-and-set, so a result that arrived first wins. Pushes nothing.
  /// </summary>
  Task<Result<DisconnectFailureBatch>> FailTasksOfDisconnectedTargetAsync(Guid targetAgentId, int limit, CancellationToken cancellationToken = default);
}

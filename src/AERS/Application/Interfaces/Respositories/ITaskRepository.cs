using System.Text.Json;
using Domain.Entities;
using Domain.Enums;

namespace Application.Interfaces.Repositories;

public interface ITaskRepository
{
  Task<AgentTask?> GetByIdAsync(Guid taskId, bool trackChanges = false, CancellationToken cancellationToken = default);

  Task AddAsync(AgentTask task, CancellationToken cancellationToken = default);

  /// <summary>
  /// Compare-and-set: one conditional UPDATE ... WHERE id = @id AND status = @expected. Returns true only when exactly one
  /// row changed. Sets started_at when next is Working and completed_at, output and error when next is terminal.
  /// Runs inside the ambient transaction (IUnitOfWork.StartTransaction) so the history row and the audit event commit with it.
  /// </summary>
  Task<bool> TryTransitionAsync(
    Guid taskId,
    AgentTaskStatus expected,
    AgentTaskStatus next,
    DateTimeOffset now,
    JsonElement? output,
    TaskError? error,
    CancellationToken cancellationToken = default);

  Task AddHistoryAsync(TaskStatusHistory entry, CancellationToken cancellationToken = default);

  /// <summary>
  /// Ids and statuses of submitted or working tasks whose deadline is before <paramref name="now"/>, oldest deadline first
  /// (WHERE status IN (0, 1) AND deadline_at &lt; @now ORDER BY deadline_at, id LIMIT @limit). Two columns, no tracking (A46).
  /// </summary>
  Task<IReadOnlyList<ExpiredTaskRef>> ListExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default);

  /// <summary>Working tasks that have this agent as target (target_agent_id index). Used when the target did not reconnect (ADR-W7-03).</summary>
  Task<IReadOnlyList<WorkingTaskRef>> ListWorkingByTargetAsync(Guid targetAgentId, int limit, CancellationToken cancellationToken = default);

  /// <summary>
  /// Terminal tasks of this caller with acknowledged_at IS NULL, ordered by (completed_at, id), at most <paramref name="limit"/>
  /// (ADR-W7-02, index ix_tasks_caller_unacknowledged). No tracking.
  /// </summary>
  Task<IReadOnlyList<AgentTask>> ListUnacknowledgedTerminalAsync(Guid callerAgentId, int limit, CancellationToken cancellationToken = default);

  /// <summary>
  /// One conditional UPDATE: WHERE id = @id AND caller_agent_id = @caller AND acknowledged_at IS NULL. Returns true when this call
  /// set acknowledged_at; false when it was already set (idempotent). Writes no history row and no audit event.
  /// </summary>
  Task<bool> TryAcknowledgeAsync(Guid taskId, Guid callerAgentId, DateTimeOffset now, CancellationToken cancellationToken = default);

  /// <summary>
  /// 1 when the agent is the target of at least one working task, otherwise 0 (a query that stops at the first row, target_agent_id
  /// index). Feeds the orphanSuspect audit flag of ADR-W8-02.
  /// </summary>
  Task<int> CountWorkingAsTargetAsync(Guid agentId, CancellationToken cancellationToken = default);
}

public sealed record ExpiredTaskRef(Guid Id, AgentTaskStatus Status);

public sealed record WorkingTaskRef(Guid Id, Guid CallerAgentId, DateTimeOffset DeadlineAt);

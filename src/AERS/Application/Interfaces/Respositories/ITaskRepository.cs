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
}

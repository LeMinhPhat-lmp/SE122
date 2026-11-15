using Application.Models;

namespace Application.Interfaces.Runtime;

/// <summary>Redis hash {prefix}task:{taskId} (A43, A44). Written after every database commit; read only by tests and, later, week 12.</summary>
public interface ITaskRuntimeStateStore
{
  /// <summary>Writes all six fields and the expiry as one atomic operation: the hash never exists without a TTL.</summary>
  Task SetAsync(TaskRuntimeState state, TimeSpan ttl, CancellationToken cancellationToken = default);

  Task<TaskRuntimeState?> GetAsync(Guid taskId, CancellationToken cancellationToken = default);
}

using Application.Models;

namespace Application.Interfaces.Policies;

public interface IRateLimiter
{
  /// <summary>
  /// Atomically consumes one task-create quota unit for the caller.
  /// Throws StateStoreUnavailableException when Redis is unavailable; the caller decides the failure mode.
  /// </summary>
  Task<RateLimitDecision> TryAcquireAsync(Guid callerAgentId, CancellationToken cancellationToken = default);
}

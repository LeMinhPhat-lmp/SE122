using Application.Models;

namespace Application.Interfaces.Policies;

/// <summary>
/// Fixed window counter per caller agent (A56). It throws <see cref="Application.Exceptions.StateStoreUnavailableException"/>
/// when Redis is unavailable and never decides a failure mode: the caller applies ADR-W8-01 (fail-closed).
/// </summary>
public interface IRateLimiter
{
  Task<RateLimitDecision> TryAcquireAsync(Guid callerAgentId, CancellationToken cancellationToken = default);
}

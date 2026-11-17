namespace Application.Interfaces.Runtime;

/// <summary>
/// Remembers when an agent lost its hub connection (ADR-W7-03): Redis sorted set {prefix}disconnected, member agentId, score unix
/// milliseconds. Temporary data only; losing it falls back to TASK_TIMEOUT.
/// </summary>
public interface IDisconnectTracker
{
  Task MarkDisconnectedAsync(Guid agentId, DateTimeOffset at, CancellationToken cancellationToken = default);

  Task ClearAsync(Guid agentId, CancellationToken cancellationToken = default);

  /// <summary>Agents whose mark is not newer than <paramref name="notAfter"/>, oldest first, at most <paramref name="limit"/>.</summary>
  Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset notAfter, int limit, CancellationToken cancellationToken = default);
}

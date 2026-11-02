using Application.Models;

namespace Application.Interfaces.Runtime;

/// <summary>Maps an agent to its current SignalR connection (decisions A30 and A31). One connection per agent, last wins.</summary>
public interface IConnectionRegistry
{
  Task BindAsync(Guid agentId, string connectionId, string instanceId, CancellationToken cancellationToken = default);

  /// <summary>Atomic compare-and-delete on connectionId: an old connection closing never removes a newer mapping.</summary>
  Task UnbindAsync(Guid agentId, string connectionId, CancellationToken cancellationToken = default);

  Task<ConnectionInfo?> ResolveAsync(Guid agentId, CancellationToken cancellationToken = default);

  /// <summary>Deletes every mapping whose instanceId equals the given id (stale mappings from a previous run).</summary>
  Task PurgeInstanceAsync(string instanceId, CancellationToken cancellationToken = default);
}

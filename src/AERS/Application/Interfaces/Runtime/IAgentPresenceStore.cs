using Application.Models;

namespace Application.Interfaces.Runtime;

public interface IAgentPresenceStore
{
  Task MarkAliveAsync(Guid agentId, DateTimeOffset now, TimeSpan aliveTtl, CancellationToken cancellationToken = default);
  Task<AgentPresence> GetAsync(Guid agentId, CancellationToken cancellationToken = default);
  Task RemoveAsync(Guid agentId, CancellationToken cancellationToken = default);

  /// <summary>Online status of many agents in one round trip. Every requested id is present in the result.</summary>
  Task<IReadOnlyDictionary<Guid, bool>> GetOnlineStatusAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken = default);
}

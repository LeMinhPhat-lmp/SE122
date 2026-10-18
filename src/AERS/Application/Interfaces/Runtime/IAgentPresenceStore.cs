using Application.Models;

namespace Application.Interfaces.Runtime;

public interface IAgentPresenceStore
{
  Task MarkAliveAsync(Guid agentId, DateTimeOffset now, TimeSpan aliveTtl, CancellationToken cancellationToken = default);
  Task<AgentPresence> GetAsync(Guid agentId, CancellationToken cancellationToken = default);
  Task RemoveAsync(Guid agentId, CancellationToken cancellationToken = default);
}

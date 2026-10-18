using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAgentCredentialRepository : IGenericRespository<AgentCredential>
{
  Task<List<AgentCredential>> ListByAgentIdAsync(Guid agentId, bool trackChanges = false, CancellationToken cancellationToken = default);

  /// <summary>Returns the credential with the given hash if it is not revoked and its agent is not deregistered (agent included).</summary>
  Task<AgentCredential?> GetActiveByHashAsync(string apiKeyHash, CancellationToken cancellationToken = default);
}

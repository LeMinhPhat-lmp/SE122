using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAgentCredentialRepository : IGenericRespository<AgentCredential>
{
  Task<AgentCredential?> GetByAgentIdAsync(Guid targetAgentId, bool trackChanges = false, CancellationToken cancellationToken = default);
}
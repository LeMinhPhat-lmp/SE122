using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAgentRepository : IGenericRespository<Agent>, ISoftDeleteRepository<Agent>
{
  /// <summary>Returns a non-deregistered agent together with its capabilities (no tracking).</summary>
  Task<Agent?> GetWithCapabilitiesAsync(Guid agentId, CancellationToken cancellationToken = default);
}

using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAgentRepository : IGenericRespository<Agent>, ISoftDeleteRepository<Agent>
{
  /// <summary>Returns a non-deregistered agent together with its capabilities (no tracking).</summary>
  Task<Agent?> GetWithCapabilitiesAsync(Guid agentId, CancellationToken cancellationToken = default);

  /// <summary>
  /// Keyset page of non-deregistered agents of one domain, ordered by (registered_at, id), with capabilities (no tracking).
  /// <paramref name="capabilityName"/> is an exact, case-sensitive match. Returns at most <paramref name="limit"/> items.
  /// </summary>
  Task<AgentPage> ListActiveAsync(string domain, string? capabilityName, DiscoveryCursorPosition? after, int limit,
    CancellationToken cancellationToken = default);
}

using Domain.Entities;

namespace Application.Interfaces.Repositories;

/// <summary>Keyset position: the (registered_at, id) of the last row already seen.</summary>
public sealed record DiscoveryCursorPosition(DateTimeOffset RegisteredAt, Guid Id);

/// <summary>One page of agents. Items never contains the extra look-ahead row.</summary>
public sealed record AgentPage(IReadOnlyList<Agent> Items, bool HasMore);

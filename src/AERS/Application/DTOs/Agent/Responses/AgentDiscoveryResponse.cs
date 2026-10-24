namespace Application.DTOs.Agent.Responses;

// Response of GET /agents (agent-discovery-result schema). NextCursor is null on the last page and is always serialized.
public sealed record AgentDiscoveryResponse(
  IReadOnlyList<AgentCardResponse> Agents,
  string? NextCursor
);

namespace Application.DTOs.Agent.Responses;

public sealed record AgentCardResponse(
  Guid AgentId,
  string Name,
  string Domain,
  string Description,
  IReadOnlyList<CapabilityResponse> Capabilities,
  string? OwnerRef
);

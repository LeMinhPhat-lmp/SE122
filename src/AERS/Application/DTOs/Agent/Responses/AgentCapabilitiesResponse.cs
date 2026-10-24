namespace Application.DTOs.Agent.Responses;

// Response of GET /agents/{id}/capabilities (agent-capabilities-response schema).
public sealed record AgentCapabilitiesResponse(
  Guid AgentId,
  IReadOnlyList<CapabilityResponse> Capabilities
);

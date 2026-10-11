namespace Application.DTOs.Agent.Responses;

public sealed record AgentResponse(
  Guid Id,
  string Name,
  string Domain,
  string Description,
  string? OwnerRef,
  DateTimeOffset LastHearbeatAt,
  IReadOnlyList<CapabilityResponse> Capabilities
);
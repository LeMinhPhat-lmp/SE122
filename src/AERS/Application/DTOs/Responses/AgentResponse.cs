namespace Application.DTOs.Request;

public sealed record AgentResponse(
  Guid Id,
  string Name,
  string Domain,
  string Description,
  string? OwnerRef,
  DateTimeOffset LastHearbeatAt,
  IReadOnlyList<CapabilitySubmission> Capabilities
);
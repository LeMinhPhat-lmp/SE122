namespace Application.DTOs.Request;

public sealed record AgentCardSubmission(
  string Name,
  string Domain,
  string Description,
  string? OwnerRef,
  IReadOnlyList<CapabilitySubmission> Capabilities
);
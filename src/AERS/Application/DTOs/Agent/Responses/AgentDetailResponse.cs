namespace Application.DTOs.Agent.Responses;

// Response of GET /agents/{id} (Protocol Spec section 1.7).
public sealed record AgentDetailResponse(
  AgentCardResponse AgentCard,
  AgentRuntimeStatusResponse RuntimeStatus
);

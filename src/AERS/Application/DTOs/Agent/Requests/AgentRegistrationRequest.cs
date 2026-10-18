namespace Application.DTOs.Agent.Requests;

// The wire property name is "agentCard" (Protocol Spec section 1.4).
public sealed record AgentRegistrationRequest(
  AgentCardSubmission AgentCard
);

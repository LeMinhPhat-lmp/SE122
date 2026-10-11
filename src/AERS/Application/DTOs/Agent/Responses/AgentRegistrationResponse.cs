namespace Application.DTOs.Agent.Responses;

public sealed record AgentRegistrationResponse(
  Guid AgentId,
  string ApiKey
);
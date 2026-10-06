namespace Application.DTOs.Responses;

public sealed record AgentRegistrationResponse(
  Guid AgentId,
  string ApiKey
);
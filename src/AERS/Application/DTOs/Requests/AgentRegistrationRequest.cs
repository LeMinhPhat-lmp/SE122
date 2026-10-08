using System.Security.Cryptography.X509Certificates;

namespace Application.DTOs.Request;

public sealed record AgentRegistrationRequest(
  AgentCardSubmission AgentCardSubmission
);
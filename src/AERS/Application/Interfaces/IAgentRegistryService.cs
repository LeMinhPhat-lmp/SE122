using Application.DTOs.Request;
using Application.DTOs.Responses;

namespace Application.Interfaces;

public interface IAgentRegistryService
{
  Task<AgentRegistrationResponse> RegisterAsync(AgentRegistrationRequest request);
  Task HeartbeatAsync(Guid agentId);
  Task DeregisterAsync(Guid agentId);
  Task<AgentResponse?> GetAsync(Guid agentId);
}
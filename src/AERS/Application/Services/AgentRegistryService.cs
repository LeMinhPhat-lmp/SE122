using Application.DTOs.Request;
using Application.DTOs.Responses;
using Application.Interfaces;

namespace Application.Services;

public class AgentRegistryService : IAgentRegistryService
{
  public Task DeregisterAsync(Guid agentId)
  {
    throw new NotImplementedException();
  }

  public Task<AgentResponse?> GetAsync(Guid agentId)
  {
    throw new NotImplementedException();
  }

  public Task HeartbeatAsync(Guid agentId)
  {
    throw new NotImplementedException();
  }

  public Task<AgentRegistrationResponse> RegisterAsync(AgentRegistrationRequest request)
  {
    throw new NotImplementedException();
  }
}
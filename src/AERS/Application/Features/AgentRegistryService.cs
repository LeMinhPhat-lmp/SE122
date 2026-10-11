using Application.DTOs.Agent.Requests;
using Application.DTOs.Agent.Responses;
using Application.Interfaces.Features;
using Application.Interfaces.Repositories;

namespace Application.Services;

public class AgentRegistryService(IUnitOfWork unitOfWork) : IAgentRegistryService
{
  public Task DeregisterAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    throw new NotImplementedException();
  }

  public Task<AgentResponse?> GetAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    throw new NotImplementedException();
  }

  public Task HeartbeatAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    throw new NotImplementedException();
  }

  public Task<AgentRegistrationResponse> RegisterAsync(AgentRegistrationRequest request, CancellationToken cancellationToken = default)
  {
    throw new NotImplementedException();
  }
}
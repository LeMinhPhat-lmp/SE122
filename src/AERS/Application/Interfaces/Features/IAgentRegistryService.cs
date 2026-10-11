using Application.DTOs.Agent.Requests;
using Application.DTOs.Agent.Responses;
namespace Application.Interfaces.Features;

public interface IAgentRegistryService
{
  Task<AgentRegistrationResponse> RegisterAsync(AgentRegistrationRequest request, CancellationToken cancellationToken = default);
  Task HeartbeatAsync(Guid agentId, CancellationToken cancellationToken = default);
  Task DeregisterAsync(Guid agentId, CancellationToken cancellationToken = default);
  Task<AgentResponse?> GetAsync(Guid agentId, CancellationToken cancellationToken = default);
}
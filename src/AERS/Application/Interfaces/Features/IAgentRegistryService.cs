using Application.DTOs.Agent.Requests;
using Application.DTOs.Agent.Responses;
using Application.DTOs.Result.Responses;
using Application.Models;

namespace Application.Interfaces.Features;

public interface IAgentRegistryService
{
  Task<Result<AgentRegistrationResponse>> RegisterAsync(AgentRegistrationRequest request, CancellationToken cancellationToken = default);
  Task<Result> HeartbeatAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);
  Task<Result> DeregisterAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);
  Task<Result<AgentDetailResponse>> GetAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);
  Task<Result<AgentDiscoveryResponse>> ListAsync(AgentQuery query, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);
  Task<Result<AgentCapabilitiesResponse>> GetCapabilitiesAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default);
}

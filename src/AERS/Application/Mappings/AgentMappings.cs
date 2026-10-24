using Application.DTOs.Agent.Responses;
using Application.Models;
using Domain.Entities;

namespace Application.Mappings;

public static class AgentMappings
{
  /// <summary>Active capabilities of the agent ordered by name (ordinal), decision A17.</summary>
  public static List<CapabilityResponse> ToCapabilityResponses(this Agent agent) => agent.Capabilities
    .Where(capability => !capability.IsDeleted)
    .OrderBy(capability => capability.Name, StringComparer.Ordinal)
    .Select(capability => new CapabilityResponse(
      capability.Name,
      capability.Description,
      capability.InputSchemaJson,
      capability.OutputSchemaJson))
    .ToList();

  public static AgentCardResponse ToCardResponse(this Agent agent) => new(
    agent.Id,
    agent.Name,
    agent.Domain,
    agent.Description,
    agent.ToCapabilityResponses(),
    agent.OwnerRef);

  public static AgentDetailResponse ToDetailResponse(this Agent agent, AgentPresence presence) => new(
    agent.ToCardResponse(),
    new AgentRuntimeStatusResponse(
      agent.Id,
      presence.IsOnline ? "online" : "offline",
      presence.LastHeartbeatAt));
}

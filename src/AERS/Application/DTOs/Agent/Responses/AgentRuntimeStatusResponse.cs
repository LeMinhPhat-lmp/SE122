namespace Application.DTOs.Agent.Responses;

/// <param name="Status">Either "online" or "offline".</param>
public sealed record AgentRuntimeStatusResponse(
  Guid AgentId,
  string Status,
  DateTimeOffset? LastHeartbeatAt
);

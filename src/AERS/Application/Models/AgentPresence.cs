namespace Application.Models;

public sealed record AgentPresence(bool IsOnline, DateTimeOffset? LastHeartbeatAt);

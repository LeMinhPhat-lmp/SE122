using Domain.Enums;

namespace Application.Models;

/// <summary>
/// Runtime state of a task in Redis (Protocol Spec section 6.4, decisions A43 and A44, ADR-W7-01). Write-only in production
/// code: PostgreSQL stays the source of truth and nothing reads this state to answer a request.
/// </summary>
public sealed record TaskRuntimeState(
  Guid TaskId,
  AgentTaskStatus Status,
  DateTimeOffset UpdatedAt,
  string? ConnectionId,
  string? InstanceId,
  DateTimeOffset? DeadlineAt);

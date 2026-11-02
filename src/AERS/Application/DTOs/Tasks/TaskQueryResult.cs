using System.Text.Json;

namespace Application.DTOs.Tasks;

/// <summary>
/// Response of GET /tasks/{taskId} (Protocol Spec section 7.2). No input, no ancestorChain, no deadlineAt.
/// Status is the lowercase wire value.
/// </summary>
public sealed record TaskQueryResult(
  string TaskId,
  string CallerAgentId,
  string TargetAgentId,
  string Capability,
  string Status,
  JsonElement? Output,
  string? ParentTaskId,
  string RootTaskId,
  int Depth,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? CompletedAt,
  ErrorPayload? Error);

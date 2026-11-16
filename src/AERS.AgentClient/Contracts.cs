using System.Text.Json;
using System.Text.Json.Serialization;

namespace AERS.AgentClient;

public sealed class AersClientOptions
{
  public required Uri BaseUrl { get; init; }
  public string? RegistrationKey { get; init; }
  public string? ApiKey { get; set; }
}

public sealed record AgentCardSubmission(
  string Name,
  string Domain,
  string Description,
  string? OwnerRef,
  IReadOnlyList<Capability> Capabilities);

public sealed record Capability(
  string Name,
  string Description,
  JsonElement InputSchema,
  JsonElement OutputSchema);

public sealed record AgentCard(
  Guid AgentId,
  string Name,
  string Domain,
  string Description,
  IReadOnlyList<Capability> Capabilities,
  string? OwnerRef);

public sealed record AgentPage(IReadOnlyList<AgentCard> Agents, string? NextCursor);

public sealed record AgentCapabilities(Guid AgentId, IReadOnlyList<Capability> Capabilities);

public sealed record AgentDetail(AgentCard AgentCard, RuntimeStatus RuntimeStatus);

public sealed record RuntimeStatus(Guid AgentId, string Status, DateTimeOffset? LastHeartbeatAt);

public sealed record AgentQuery(string? Domain, string? Capability, bool OnlineOnly, int? Limit);

public sealed record RegistrationResult(Guid AgentId, string ApiKey);

public enum TaskStatusValue
{
  Submitted,
  Working,
  Completed,
  Failed
}

public sealed record AersError(
  string Code,
  string Message,
  JsonElement? Details = null,
  bool? Retryable = null);

public sealed record TaskEnvelope(string MessageType, JsonElement Payload);

public sealed record CreateTaskCall(
  Guid TargetAgentId,
  string Capability,
  JsonElement Input,
  Guid? ParentTaskId = null);

public sealed record TaskAck(
  bool Accepted,
  Guid? TaskId,
  TaskStatusValue? Status,
  AersError? Error);

public sealed record TaskOutcome(
  Guid TaskId,
  TaskStatusValue Status,
  JsonElement? Output,
  AersError? Error);

public sealed record InvocationResult(JsonElement? Output, AersError? Error);

public sealed record InvocationContext(AersAgentConnection Connection);

public sealed record PolicyContext(
  Guid CallerAgentId,
  Guid TargetAgentId,
  string Capability,
  string CallerDomain,
  string TargetDomain,
  Guid RootTaskId,
  Guid? ParentTaskId,
  int Depth,
  string? OwnerRef);

public sealed record TaskInvocation(
  Guid TaskId,
  Guid RootTaskId,
  Guid? ParentTaskId,
  int Depth,
  string Capability,
  JsonElement Input,
  PolicyContext PolicyContext);

public sealed record TaskQueryResult(
  Guid TaskId,
  Guid CallerAgentId,
  Guid TargetAgentId,
  string Capability,
  TaskStatusValue Status,
  JsonElement? Output,
  Guid? ParentTaskId,
  Guid RootTaskId,
  int Depth,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? CompletedAt,
  AersError? Error);

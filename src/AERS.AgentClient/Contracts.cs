using System.Text.Json;
using System.Text.Json.Serialization;

namespace AERS.AgentClient;

public sealed class AersClientOptions
{
  public required Uri BaseUrl { get; init; }
  public string? RegistrationKey { get; set; }
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

public sealed record StoredCredential(Guid AgentId, string ApiKey);

public sealed record TaskOutcome(string Status, JsonElement? Output, TaskError? Error);

public sealed record TaskError(string Code, string Message, JsonElement? Details = null, bool Retryable = false);

public sealed record TaskQueryResult(
  Guid TaskId,
  Guid CallerAgentId,
  Guid TargetAgentId,
  string Capability,
  string Status,
  JsonElement? Output,
  Guid? ParentTaskId,
  Guid RootTaskId,
  int Depth,
  DateTimeOffset CreatedAt,
  DateTimeOffset? StartedAt,
  DateTimeOffset? CompletedAt,
  TaskError? Error);

public sealed record TaskInvocation(
  Guid TaskId,
  Guid RootTaskId,
  Guid? ParentTaskId,
  int Depth,
  string Capability,
  JsonElement Input,
  JsonElement? PolicyContext);

public sealed record InvocationResult(
  string Status,
  JsonElement? Output,
  TaskError? Error,
  bool SuppressReply = false);

public sealed class AersAgentConnectionOptions
{
  public required Uri HubUrl { get; init; }
  public string? ApiKey { get; set; }
  public TimeSpan ResultRetention { get; init; } = TimeSpan.FromMinutes(10);
  public TimeSpan ObserveInterval { get; init; } = TimeSpan.FromSeconds(2);
  public int ResultSendRetryCount { get; init; } = 3;
  public TimeSpan ResultSendRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
}

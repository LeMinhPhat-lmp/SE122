using System.Text.Json;

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

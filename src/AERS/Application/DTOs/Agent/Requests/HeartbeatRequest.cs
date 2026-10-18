namespace Application.DTOs.Agent.Requests;

/// <summary>
/// Optional heartbeat body (Protocol Spec section 2.1). The timestamp is only checked for format;
/// AERS always uses its own clock.
/// </summary>
public sealed record HeartbeatRequest(
  DateTimeOffset? Timestamp = null
);

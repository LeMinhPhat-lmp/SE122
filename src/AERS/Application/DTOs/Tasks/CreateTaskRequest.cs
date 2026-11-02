using System.Text.Json;

namespace Application.DTOs.Tasks;

/// <summary>
/// Payload of task.create (Protocol Spec section 5.1). Identifiers stay strings on purpose: the service validates them
/// and reports every violation together with its JSON Pointer path. callerAgentId is never part of a payload.
/// </summary>
public sealed record CreateTaskRequest(string TargetAgentId, string Capability, JsonElement Input, string? ParentTaskId = null);

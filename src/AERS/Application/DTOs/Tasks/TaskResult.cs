using System.Text.Json;

namespace Application.DTOs.Tasks;

/// <summary>Payload of task.result (Protocol Spec section 5.2): target to AERS and AERS to caller. All four members are always serialized.</summary>
public sealed record TaskResult(string TaskId, string Status, JsonElement? Output, ErrorPayload? Error);

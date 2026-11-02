using System.Text.Json;

namespace Application.DTOs.Tasks;

/// <summary>Wire envelope (Protocol Spec section 6.2). Payload is validated against the schema of the message type by ITaskMessageService.</summary>
public sealed record TaskEnvelope(string MessageType, string? CorrelationId, JsonElement Payload);

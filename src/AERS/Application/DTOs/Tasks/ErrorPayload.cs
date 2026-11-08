using System.Text.Json;

namespace Application.DTOs.Tasks;

/// <summary>Wire shape of error.schema.json (Protocol Spec section 12.1) used inside acks and task.result payloads.</summary>
public sealed record ErrorPayload(string Code, string Message, JsonElement? Details = null, bool? Retryable = null);

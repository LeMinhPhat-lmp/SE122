using System.Text.Json;

namespace Domain.Entities;

/// <summary>Error of a failed Task (Protocol Spec section 12.1). Stored as jsonb on the task row.</summary>
public sealed record TaskError(string Code, string Message, JsonElement? Details = null, bool? Retryable = null);

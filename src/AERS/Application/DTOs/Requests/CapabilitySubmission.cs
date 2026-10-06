using System.Text.Json;

namespace Application.DTOs.Request;

public sealed record CapabilitySubmission(
  string Name,
  string Description,
  JsonElement InputSchema,
  JsonElement OutputSchema
);
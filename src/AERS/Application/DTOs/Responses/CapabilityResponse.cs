using System.Text.Json;

namespace Application.DTOs.Request;

public sealed record CapabilityResponse(
  Guid Id,
  string Name,
  string Description,
  JsonElement InputSchema,
  JsonElement OutputSchema
);
using System.Text.Json;

namespace Application.DTOs.Agent.Responses;

public sealed record CapabilityResponse(
  string Name,
  string Description,
  JsonElement InputSchema,
  JsonElement OutputSchema
);

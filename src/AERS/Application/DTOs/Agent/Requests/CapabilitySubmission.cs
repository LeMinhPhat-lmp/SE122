using System.Text.Json;

namespace Application.DTOs.Agent.Requests;

public sealed record CapabilitySubmission(
  string Name,
  string Description,
  JsonElement InputSchema,
  JsonElement OutputSchema
);
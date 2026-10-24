namespace Application.DTOs.Agent.Requests;

/// <summary>Raw (not yet validated) query of GET /agents. Validation and normalization happen in the service.</summary>
public sealed record AgentQuery(string? Domain, string? Capability, bool OnlineOnly, int? Limit, string? Cursor);

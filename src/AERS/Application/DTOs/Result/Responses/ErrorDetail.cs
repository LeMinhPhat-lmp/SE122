namespace Application.DTOs.Result.Responses;

/// <param name="Path">JSON Pointer to the offending value, for example /agentCard/capabilities/0/name.</param>
public sealed record ErrorDetail(string Path, string Message);

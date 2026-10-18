namespace API.DTOs.ApiContract.Responses;

/// <summary>Body of every error response, matching error.schema.json (Protocol Spec sections 12.1 and 12.3).</summary>
public sealed record ErrorResponse(
  string Code,
  string Message,
  ErrorResponseDetails? Details = null,
  bool? Retryable = null
);

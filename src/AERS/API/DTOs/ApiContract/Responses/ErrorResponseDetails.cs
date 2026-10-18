using Application.DTOs.Result.Responses;

namespace API.DTOs.ApiContract.Responses;

/// <summary>The "details" object of an error response: { "errors": [ { path, message } ] }.</summary>
public sealed record ErrorResponseDetails(IReadOnlyList<ErrorDetail> Errors);

using API.DTOs.ApiContract.Responses;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Microsoft.AspNetCore.Mvc;

namespace API.Extensions;

/// <summary>
/// Single translation point from a failed <see cref="Result"/> to HTTP. Controllers, the global exception handler,
/// the authentication challenge and the model-binding error factory all go through these methods.
/// </summary>
public static class ResultExtensions
{
  public static int ToStatusCode(this ResultStatus status) => status switch
  {
    ResultStatus.Success => StatusCodes.Status200OK,
    ResultStatus.BadRequest or ResultStatus.ValidationError => StatusCodes.Status400BadRequest,
    ResultStatus.Unauthorized => StatusCodes.Status401Unauthorized,
    ResultStatus.Forbidden => StatusCodes.Status403Forbidden,
    ResultStatus.NotFound => StatusCodes.Status404NotFound,
    ResultStatus.Conflict => StatusCodes.Status409Conflict,
    _ => StatusCodes.Status500InternalServerError
  };

  public static ErrorResponse ToErrorResponse(this Result result) => new(
    result.ErrorCode ?? ErrorCodes.InternalError,
    result.Errors.FirstOrDefault() ?? "The request failed.",
    result.ErrorDetails.Count == 0 ? null : new ErrorResponseDetails(result.ErrorDetails));

  /// <summary>For controller actions.</summary>
  public static ObjectResult ToErrorResult(this Result result) =>
    new(result.ToErrorResponse()) { StatusCode = result.ResultStatus.ToStatusCode() };

  /// <summary>For code that runs outside MVC (exception handler, authentication challenge).</summary>
  public static async Task WriteErrorAsync(this HttpResponse response, Result result, CancellationToken cancellationToken)
  {
    response.StatusCode = result.ResultStatus.ToStatusCode();
    await response.WriteAsJsonAsync(result.ToErrorResponse(), cancellationToken);
  }
}

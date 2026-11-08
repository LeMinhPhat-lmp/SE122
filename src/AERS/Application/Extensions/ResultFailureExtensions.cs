using Application.Constants;
using Application.DTOs.Result.Responses;

namespace Application.Extensions;

public static class ResultFailureExtensions
{
  /// <summary>Carries a failed Result over to a Result of another type without losing status, code, message or details.</summary>
  public static Result<T> ToFailure<T>(this Result failure) =>
    Result<T>.Failure(
      failure.ResultStatus,
      failure.ErrorCode ?? ErrorCodes.InternalError,
      failure.Errors.FirstOrDefault() ?? "The request failed.",
      failure.ErrorDetails);
}

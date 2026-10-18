using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.Interfaces.Errors;

namespace API.Exceptions;

/// <summary>
/// Kestrel throws BadHttpRequestException when the body exceeds Aers:RegistrationLimits:MaxBodyBytes or cannot be read.
/// Only the API layer knows this type, so the API layer classifies it (spec: 400 VALIDATION_ERROR).
/// </summary>
public sealed class BadHttpRequestExceptionClassifier : IExceptionClassifier
{
  public Result? Classify(Exception exception) =>
    exception is BadHttpRequestException
      ? Result.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request body could not be read or exceeds the maximum allowed size.")
      : null;
}

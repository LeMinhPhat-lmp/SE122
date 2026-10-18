using System.Text.Json;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using FluentValidation.Results;

namespace Application.Extensions;

public static class ValidationResultExtensions
{
  /// <summary>Turns FluentValidation failures into a VALIDATION_ERROR result with one detail per failure.</summary>
  public static Result<T> ToFailure<T>(this ValidationResult validation) =>
    Result<T>.Failure(
      ResultStatus.ValidationError,
      ErrorCodes.ValidationError,
      "The request payload is invalid.",
      validation.Errors
        .Select(failure => new ErrorDetail(
          // "AgentCard.Capabilities[0].Name" becomes "/agentCard/capabilities/0/name"
          "/" + string.Join('/', failure.PropertyName.Replace("[", ".").Replace("]", "").Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)),
          failure.ErrorMessage))
        .ToList());
}

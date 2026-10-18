using Application.DTOs.Result.Responses;

namespace Application.Interfaces.Errors;

/// <summary>
/// Recognizes the exceptions one layer knows about. Each layer contributes its own classifier because only
/// that layer knows what its exceptions mean.
/// </summary>
public interface IExceptionClassifier
{
  /// <returns>A failed result, or null when this classifier does not recognize the exception.</returns>
  Result? Classify(Exception exception);
}

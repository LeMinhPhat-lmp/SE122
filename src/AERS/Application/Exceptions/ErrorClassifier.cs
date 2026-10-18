using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.Interfaces.Errors;

namespace Application.Exceptions;

/// <summary>Chain of responsibility over the classifiers registered by every layer.</summary>
public sealed class ErrorClassifier(IEnumerable<IExceptionClassifier> classifiers) : IErrorClassifier
{
  public Result Classify(Exception exception)
  {
    // Wrapped exceptions are classified by whichever layer understands them, starting with the outermost.
    for (var current = exception; current is not null; current = current.InnerException)
      foreach (var classifier in classifiers)
        if (classifier.Classify(current) is { } failure)
          return failure;

    return Result.Failure(ResultStatus.InternalError, ErrorCodes.InternalError, "An unexpected error occurred.");
  }
}

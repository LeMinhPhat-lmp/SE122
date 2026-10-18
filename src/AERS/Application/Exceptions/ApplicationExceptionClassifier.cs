using Application.DTOs.Result.Responses;
using Application.Interfaces.Errors;

namespace Application.Exceptions;

public sealed class ApplicationExceptionClassifier : IExceptionClassifier
{
  public Result? Classify(Exception exception) =>
    exception is ApplicationException applicationException
      ? Result.Failure(applicationException.Status, applicationException.Code, applicationException.Message)
      : null;
}

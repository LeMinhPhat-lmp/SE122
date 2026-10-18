using Application.DTOs.Result.Responses;

namespace Application.Interfaces.Errors;

public interface IErrorClassifier
{
  /// <summary>Always returns a failed result; unrecognized exceptions become INTERNAL_ERROR.</summary>
  Result Classify(Exception exception);
}

using Application.DTOs.Result.Enums;

namespace Application.Exceptions;

/// <summary>
/// A technical failure that has business meaning. The layer that understands the technical exception wraps it
/// (as the inner exception) so other layers can reason about it without knowing the underlying technology.
/// </summary>
public abstract class ApplicationException : Exception
{
  public string Code { get; }
  public ResultStatus Status { get; }

  public ApplicationException(string code, string message, Exception innerException, ResultStatus status = ResultStatus.InternalError) : base(message, innerException)
  {
    Code = code;
    Status = status;
  }
}

namespace Application.Exceptions;

public abstract class ApplicationException : Exception
{
  public string Code { get; }
  public ApplicationException(string code, string message, Exception innerException) : base(message, innerException)
  {
    Code = code;
  }
}
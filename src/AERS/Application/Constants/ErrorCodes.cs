namespace Application.Constants;

// Baseline error codes, see Protocol-Specification-v3.md section 12.2.
public static class ErrorCodes
{
  public const string AuthenticationFailed = "AUTHENTICATION_FAILED";
  public const string AgentNotFound = "AGENT_NOT_FOUND";
  public const string DomainForbidden = "DOMAIN_FORBIDDEN";
  public const string AgentAccessForbidden = "AGENT_ACCESS_FORBIDDEN";
  public const string ValidationError = "VALIDATION_ERROR";
  public const string InternalError = "INTERNAL_ERROR";
}

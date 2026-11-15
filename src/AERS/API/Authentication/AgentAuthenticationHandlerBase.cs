using System.Text.Encodings.Web;
using API.Extensions;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace API.Authentication;

/// <summary>
/// Missing, wrong or revoked credentials all produce the same 401 AUTHENTICATION_FAILED response
/// so the cause is never revealed (Protocol Spec section 8.5).
/// </summary>
public abstract class AgentAuthenticationHandlerBase(
  IOptionsMonitor<AuthenticationSchemeOptions> options,
  ILoggerFactory logger,
  UrlEncoder encoder,
  AuthFailureLogLimiter logLimiter) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
  public const string Missing = "missing";
  public const string Malformed = "malformed";
  public const string UnknownOrRevoked = "unknown_or_revoked";

  /// <summary>
  /// ADR-W8-04: one rate limited structured warning, never the credential (raw or hashed), never a header, and nothing in the
  /// database (a flood would fill audit_events).
  /// </summary>
  protected void LogFailure(string failureClass)
  {
    var remote = Context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!logLimiter.ShouldLog(remote, failureClass))
      return;

    Logger.LogWarning(
      "Authentication failed: scheme {Scheme}, reason {Reason}, path {Path}, remote address {RemoteAddress}, hub {IsHub}.",
      Scheme.Name, failureClass, Request.Path.Value, remote, Request.Path.StartsWithSegments("/hubs"));
  }

  protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
  {
    await Response.WriteErrorAsync(
      Result.Failure(ResultStatus.Unauthorized, ErrorCodes.AuthenticationFailed, "Authentication failed."),
      Context.RequestAborted);
  }
}

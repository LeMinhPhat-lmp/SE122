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
  UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
  protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
  {
    await Response.WriteErrorAsync(
      Result.Failure(ResultStatus.Unauthorized, ErrorCodes.AuthenticationFailed, "Authentication failed."),
      Context.RequestAborted);
  }
}

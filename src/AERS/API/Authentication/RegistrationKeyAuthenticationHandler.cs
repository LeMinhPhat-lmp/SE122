using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Interfaces.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace API.Authentication;

/// <summary>
/// X-Registration-Key: valid only for POST /agents/register. The caller has no agent identity yet
/// (Protocol Spec section 8.3).
/// </summary>
public sealed class RegistrationKeyAuthenticationHandler(
  IOptionsMonitor<AuthenticationSchemeOptions> options,
  ILoggerFactory logger,
  UrlEncoder encoder,
  IRegistrationKeyValidator registrationKeyValidator) : AgentAuthenticationHandlerBase(options, logger, encoder)
{
  public const string HeaderName = "X-Registration-Key";

  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue(HeaderName, out var header))
      return Task.FromResult(AuthenticateResult.NoResult());

    if (!registrationKeyValidator.IsValid(header.ToString()))
      return Task.FromResult(AuthenticateResult.Fail("Invalid credential."));

    var identity = new ClaimsIdentity(
      new[] { new Claim(AgentClaimTypes.AuthScheme, "API_KEY") },
      Scheme.Name);

    return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
  }
}

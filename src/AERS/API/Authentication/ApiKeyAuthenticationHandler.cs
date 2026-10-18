using System.Security.Claims;
using System.Text.Encodings.Web;
using Application.Interfaces.Repositories;
using Application.Interfaces.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace API.Authentication;

/// <summary>X-Api-Key: the agent identity is always derived from the credential (Protocol Spec sections 8.1 and 8.2).</summary>
public sealed class ApiKeyAuthenticationHandler(
  IOptionsMonitor<AuthenticationSchemeOptions> options,
  ILoggerFactory logger,
  UrlEncoder encoder,
  IApiKeyService apiKeyService,
  IUnitOfWork unitOfWork) : AgentAuthenticationHandlerBase(options, logger, encoder)
{
  public const string HeaderName = "X-Api-Key";

  protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    if (!Request.Headers.TryGetValue(HeaderName, out var header))
      return AuthenticateResult.NoResult();

    var rawKey = header.ToString();
    if (string.IsNullOrWhiteSpace(rawKey))
      return AuthenticateResult.Fail("Invalid credential.");

    // Credential values (raw or hashed) must never be written to logs.
    var credential = await unitOfWork.AgentCredentialRepository.GetActiveByHashAsync(apiKeyService.Hash(rawKey), Context.RequestAborted);
    if (credential is null)
      return AuthenticateResult.Fail("Invalid credential.");

    var identity = new ClaimsIdentity(
      new[]
      {
        new Claim(AgentClaimTypes.AgentId, credential.AgentId.ToString()),
        new Claim(AgentClaimTypes.Domain, credential.Agent.Domain),
        new Claim(AgentClaimTypes.AuthScheme, "API_KEY")
      },
      Scheme.Name);

    return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
  }
}

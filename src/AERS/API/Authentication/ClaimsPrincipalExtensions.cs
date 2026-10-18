using System.Security.Claims;
using Application.Models;

namespace API.Authentication;

public static class ClaimsPrincipalExtensions
{
  public static AuthenticatedAgentContext ToAgentContext(this ClaimsPrincipal user)
  {
    Guid? agentId = Guid.TryParse(user.FindFirstValue(AgentClaimTypes.AgentId), out var parsed) ? parsed : null;

    return new AuthenticatedAgentContext(
      agentId,
      user.FindFirstValue(AgentClaimTypes.Domain),
      user.FindFirstValue(AgentClaimTypes.AuthScheme));
  }
}

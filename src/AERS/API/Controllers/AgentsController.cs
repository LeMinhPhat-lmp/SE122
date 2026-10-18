using API.Authentication;
using API.DTOs.ApiContract.Responses;
using API.Extensions;
using Application.DTOs.Agent.Requests;
using Application.DTOs.Agent.Responses;
using Application.Interfaces.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace API.Controllers;

// Endpoints follow Protocol-Specification-v3.md section 2.3.
// Binding and validation errors are turned into responses by the API behavior options; failed results are turned
// into responses by ResultExtensions; unexpected exceptions are handled by GlobalExceptionHandler.
[ApiController]
[Route("agents")]
public sealed class AgentsController(IAgentRegistryService registryService) : ControllerBase
{
  // NoStore adds "Cache-Control: no-store" because the response contains a secret (apiKey).
  [HttpPost("register")]
  [Authorize(AuthenticationSchemes = AuthSchemes.RegistrationKey)]
  [ResponseCache(NoStore = true)]
  [ProducesResponseType<AgentRegistrationResponse>(StatusCodes.Status201Created)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
  public async Task<IActionResult> RegisterAgentAsync([FromBody] AgentRegistrationRequest request, CancellationToken cancellationToken)
  {
    var result = await registryService.RegisterAsync(request, cancellationToken);
    if (!result.IsSuccess)
      return result.ToErrorResult();

    return CreatedAtRoute(RouteNames.GetAgent, new { agentId = result.Data!.AgentId }, result.Data);
  }

  // The body is optional; it is bound only so that strict JSON rules reject malformed or unknown content.
  [HttpPost("{agentId}/heartbeat")]
  [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> CheckAgentHeartbeatAsync(
    Guid agentId,
    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] HeartbeatRequest? request,
    CancellationToken cancellationToken)
  {
    var result = await registryService.HeartbeatAsync(agentId, User.ToAgentContext(), cancellationToken);
    return result.IsSuccess ? NoContent() : result.ToErrorResult();
  }

  [HttpGet("{agentId}", Name = RouteNames.GetAgent)]
  [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
  [ProducesResponseType<AgentDetailResponse>(StatusCodes.Status200OK)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetAgentAsync(Guid agentId, CancellationToken cancellationToken)
  {
    var result = await registryService.GetAsync(agentId, User.ToAgentContext(), cancellationToken);
    return result.IsSuccess ? Ok(result.Data) : result.ToErrorResult();
  }

  // The body must be empty or "{}"; DeregistrationRequest has no members so any property is rejected.
  [HttpDelete("{agentId}")]
  [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> DeregisterAgentAsync(
    Guid agentId,
    [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeregistrationRequest? request,
    CancellationToken cancellationToken)
  {
    var result = await registryService.DeregisterAsync(agentId, User.ToAgentContext(), cancellationToken);
    return result.IsSuccess ? NoContent() : result.ToErrorResult();
  }
}

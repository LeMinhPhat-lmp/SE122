using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentsController : ControllerBase
{
  [HttpPost("/register")]
  public async Task<IActionResult> RegisterAgentAsync()
  {
    throw new NotImplementedException();
  }

  [HttpPost("/{agentId:guid}/heartbeat")]
  public async Task<IActionResult> CheckAgentHeartbeatAsync(Guid agentId)
  {
    throw new NotImplementedException();
  }

  [HttpGet("/{agentId:guid}")]
  public async Task<IActionResult> GetAgentAsync(Guid agentId)
  {
    throw new NotImplementedException();
  }

  [HttpDelete("/{agentId:guid}/deregister")]
  public async Task<IActionResult> DeregisterAgentAsync(Guid agentId)
  {
    throw new NotImplementedException();
  }
}
using Application.Interfaces.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(IDependencyHealthChecker healthChecker) : ControllerBase
{
  // No credential required. Returns 200 when PostgreSQL and Redis are reachable, otherwise 503 (spec section 2.3).
  [HttpGet]
  public async Task<IActionResult> GetAsync(CancellationToken cancellationToken)
  {
    var healthy = await healthChecker.IsHealthyAsync(cancellationToken);
    return healthy
      ? Ok(new { status = "healthy" })
      : StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unhealthy" });
  }
}

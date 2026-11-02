using API.Authentication;
using API.DTOs.ApiContract.Responses;
using API.Extensions;
using Application.DTOs.Tasks;
using Application.Interfaces.Features;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Endpoint follows Protocol-Specification-v3.md section 7 and decision A33.
[ApiController]
[Route("tasks")]
public sealed class TasksController(ITaskService taskService) : ControllerBase
{
  [HttpGet("{taskId}")]
  [Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
  [EndpointSummary("Get the current state and result of a task")]
  [EndpointDescription("Reads the task from PostgreSQL. Only the caller agent or the target agent of the task may read it. " +
    "The response contains no input, ancestorChain or deadlineAt; optional values are explicit null. " +
    "Use it to recover a result that was pushed while the caller was disconnected.")]
  [ProducesResponseType<TaskQueryResult>(StatusCodes.Status200OK)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status403Forbidden)]
  [ProducesResponseType<ErrorResponse>(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetTaskAsync(Guid taskId, CancellationToken cancellationToken)
  {
    var result = await taskService.GetAsync(taskId, User.ToAgentContext(), cancellationToken);
    return result.IsSuccess ? Ok(result.Data) : result.ToErrorResult();
  }
}

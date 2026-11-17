using System.Text.Json;
using Application.Constants;
using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;
using Application.Tasks;
using Domain.Entities;
using Domain.Enums;

namespace Application.Mappings;

public static class TaskMappings
{
  public static string ToWire(this AgentTaskStatus status) => status switch
  {
    AgentTaskStatus.Submitted => "submitted",
    AgentTaskStatus.Working => "working",
    AgentTaskStatus.Completed => "completed",
    AgentTaskStatus.Failed => "failed",
    _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
  };

  public static AgentTaskStatus FromWire(string status) => status switch
  {
    "submitted" => AgentTaskStatus.Submitted,
    "working" => AgentTaskStatus.Working,
    "completed" => AgentTaskStatus.Completed,
    "failed" => AgentTaskStatus.Failed,
    _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.")
  };

  public static ErrorPayload ToPayload(this TaskError error) => new(error.Code, error.Message, error.Details, error.Retryable);

  public static TaskError ToTaskError(this ErrorPayload error) => new(error.Code, error.Message, error.Details, error.Retryable);

  /// <summary>Single place where a failed Result becomes the wire error object (used for envelope acks).</summary>
  public static ErrorPayload ToErrorPayload(this Result failure)
  {
    // Structured details (A57) replace the per-field error list; every other code keeps { "errors": [...] }.
    var details = failure.StructuredDetails;
    if (details is null && failure.ErrorDetails.Count > 0)
      details = JsonSerializer.SerializeToElement(new { errors = failure.ErrorDetails }, EnvelopeJson.Options);

    return new ErrorPayload(
      failure.ErrorCode ?? ErrorCodes.InternalError,
      failure.Errors.FirstOrDefault() ?? "The request failed.",
      details,
      failure.Retryable);
  }

  public static TaskQueryResult ToQueryResult(this AgentTask task) => new(
    task.Id.ToString(),
    task.CallerAgentId.ToString(),
    task.TargetAgentId.ToString(),
    task.CapabilityName,
    task.Status.ToWire(),
    task.Output,
    task.ParentTaskId?.ToString(),
    task.RootTaskId.ToString(),
    task.Depth,
    task.CreatedAt,
    task.StartedAt,
    task.CompletedAt,
    task.Error?.ToPayload());

  /// <summary>The invocation sent to the target. It deliberately carries no ancestorChain (Spec section 6.3).</summary>
  public static TaskInvocation ToInvocation(this AgentTask task, Agent? caller, Agent? target) => new(
    task.Id.ToString(),
    task.RootTaskId.ToString(),
    task.ParentTaskId?.ToString(),
    task.Depth,
    task.CapabilityName,
    task.Input,
    new PolicyContext(
      task.CallerAgentId.ToString(),
      task.TargetAgentId.ToString(),
      task.CapabilityName,
      caller?.Domain,
      target?.Domain,
      task.RootTaskId.ToString(),
      task.ParentTaskId?.ToString(),
      task.Depth,
      target?.OwnerRef));

  public static TaskResult ToResult(this AgentTask task) => new(
    task.Id.ToString(),
    task.Status.ToWire(),
    task.Output,
    task.Error?.ToPayload());
}

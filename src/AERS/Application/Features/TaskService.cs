using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;
using Application.Exceptions;
using Application.Extensions;
using Application.Interfaces.Features;
using Application.Interfaces.Policies;
using Application.Interfaces.Realtime;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Mappings;
using Application.Models;
using Application.Options;
using Application.Tasks;
using Domain.Constants;
using Domain.Entities;
using Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services;

public sealed class TaskService(
  IUnitOfWork unitOfWork,
  ITaskPolicyEvaluator policyEvaluator,
  IConnectionRegistry connectionRegistry,
  IMessageRelay messageRelay,
  IOptions<TaskOptions> taskOptions,
  TimeProvider timeProvider,
  ILogger<TaskService> logger) : ITaskService
{
  // Same pattern as registration (spec section 1.2.1); \z instead of $ so a trailing newline is not accepted.
  private static readonly Regex CapabilityPattern = new(@"^[A-Za-z][A-Za-z0-9_.-]{0,99}\z", RegexOptions.Compiled);

  private static Result<T> Unauthenticated<T>() =>
    Result<T>.Failure(ResultStatus.Unauthorized, ErrorCodes.AuthenticationFailed, "Authentication failed.");

  // ---------------------------------------------------------------------------------------------------------------
  // A-02: create
  // ---------------------------------------------------------------------------------------------------------------

  public async Task<Result<AgentTask>> CreateAsync(CreateTaskRequest request, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default)
  {
    // Order (decisions A20, A26, A35): authenticate, shape (400), caller active, target exists, parent rules, policy, create.
    if (caller.AgentId is not { } callerId)
      return Unauthenticated<AgentTask>();

    var options = taskOptions.Value;
    var errors = new List<ErrorDetail>();

    if (!Guid.TryParseExact(request.TargetAgentId, "D", out var targetId))
      errors.Add(new ErrorDetail("/payload/targetAgentId", "targetAgentId must be a UUID."));

    if (!CapabilityPattern.IsMatch(request.Capability ?? string.Empty))
      errors.Add(new ErrorDetail("/payload/capability", "capability is not a valid capability name."));

    if (request.Input.ValueKind != JsonValueKind.Object)
      errors.Add(new ErrorDetail("/payload/input", "input must be an object."));
    else if (Encoding.UTF8.GetByteCount(request.Input.GetRawText()) > options.MaxInputBytes)
      errors.Add(new ErrorDetail("/payload/input", $"input must not exceed {options.MaxInputBytes} bytes."));

    Guid? parentTaskId = null;
    if (request.ParentTaskId is not null)
    {
      if (Guid.TryParseExact(request.ParentTaskId, "D", out var parsedParentId))
        parentTaskId = parsedParentId;
      else
        errors.Add(new ErrorDetail("/payload/parentTaskId", "parentTaskId must be a UUID."));
    }

    if (errors.Count > 0)
      return Result<AgentTask>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.", errors);

    var callerAgent = await unitOfWork.AgentRepository.GetByIdAsync(callerId, trackChanges: false, cancellationToken);
    if (callerAgent is null)
      return Unauthenticated<AgentTask>();

    var targetAgent = await unitOfWork.AgentRepository.GetWithCapabilitiesAsync(targetId, cancellationToken);
    if (targetAgent is null)
    {
      var notFound = Result.Failure(ResultStatus.NotFound, ErrorCodes.AgentNotFound, "The target agent does not exist or has been deregistered.");
      await AuditRejectionAsync(callerId, notFound, cancellationToken);
      return notFound.ToFailure<AgentTask>();
    }

    AgentTask? parent = null;
    if (parentTaskId is { } parentId)
    {
      parent = await unitOfWork.TaskRepository.GetByIdAsync(parentId, trackChanges: false, cancellationToken);
      if (parent is null)
        return Result<AgentTask>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The parent task does not exist.");
      if (parent.TargetAgentId != callerId)
        return Result<AgentTask>.Failure(ResultStatus.Forbidden, ErrorCodes.TaskAccessForbidden, "Only the target agent of the parent task can create child tasks.");
      if (parent.Status != AgentTaskStatus.Working)
        return Result<AgentTask>.Failure(ResultStatus.Conflict, ErrorCodes.TaskStateConflict, "The parent task is not in the working state.");
    }

    var policy = policyEvaluator.Evaluate(new TaskPolicyRequest(callerAgent, targetAgent, request.Capability!, request.Input));
    if (!policy.IsSuccess)
    {
      await AuditRejectionAsync(callerId, policy, cancellationToken);
      return policy.ToFailure<AgentTask>();
    }

    // Ancestry (decision A26). It is derived from the parent row, never from the client.
    var now = timeProvider.GetUtcNow();
    var taskId = Guid.NewGuid();
    List<Guid> ancestorChain = parent is null ? new() { callerId } : new(parent.AncestorChain) { parent.TargetAgentId };
    var task = AgentTask.Create(
      taskId,
      callerId,
      targetId,
      request.Capability!,
      request.Input,
      parentTaskId,
      rootTaskId: parent?.RootTaskId ?? taskId,
      depth: parent is null ? 0 : parent.Depth + 1,
      ancestorChain: ancestorChain,
      deadlineAt: now.AddSeconds(options.DefaultTaskTimeoutSeconds),
      createdAt: now);

    // One SaveChanges is one transaction: the task row and its audit event commit together. No Task exists on any failure above.
    var details = JsonSerializer.SerializeToElement(new { targetAgentId = targetId, capability = request.Capability }, EnvelopeJson.Options);
    await unitOfWork.TaskRepository.AddAsync(task, cancellationToken);
    await unitOfWork.AuditEventRepository.AddAsync(AuditEvent.Create(AuditEventTypes.TaskCreated, now, callerId, taskId, details), cancellationToken);
    await unitOfWork.SaveChangesAsync(cancellationToken);

    return Result<AgentTask>.Success(task);
  }

  // ---------------------------------------------------------------------------------------------------------------
  // A-03: dispatch
  // ---------------------------------------------------------------------------------------------------------------

  public async Task<Result<TaskDispatchOutcome>> DispatchAsync(AgentTask task, CancellationToken cancellationToken = default)
  {
    // A registry outage means the target cannot be reached; leaving the task submitted forever would be worse than failing it.
    ConnectionInfo? connection = null;
    try
    {
      connection = await connectionRegistry.ResolveAsync(task.TargetAgentId, cancellationToken);
    }
    catch (StateStoreUnavailableException ex)
    {
      logger.LogWarning(ex, "The connection registry is unavailable while dispatching task {TaskId}.", task.Id);
    }

    if (connection is null)
      return await FailWithTargetUnavailableAsync(task, AgentTaskStatus.Submitted, cancellationToken);

    var callerAgent = await unitOfWork.AgentRepository.GetByIdAsync(task.CallerAgentId, trackChanges: false, cancellationToken);
    var targetAgent = await unitOfWork.AgentRepository.GetByIdAsync(task.TargetAgentId, trackChanges: false, cancellationToken);

    // submitted -> working is committed BEFORE anything is sent (decision A27).
    if (!await TransitionAsync(task.Id, AgentTaskStatus.Submitted, AgentTaskStatus.Working, timeProvider.GetUtcNow(), null, null, cancellationToken))
      return await CurrentOutcomeAsync(task.Id, cancellationToken);

    var envelope = new TaskEnvelope(
      TaskMessageTypes.Invoke,
      null,
      JsonSerializer.SerializeToElement(task.ToInvocation(callerAgent, targetAgent), EnvelopeJson.Options));

    try
    {
      await messageRelay.SendAsync(connection, envelope, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not relay task {TaskId} to its target agent.", task.Id);
      return await FailWithTargetUnavailableAsync(task, AgentTaskStatus.Working, cancellationToken);
    }

    return Result<TaskDispatchOutcome>.Success(new TaskDispatchOutcome(task.Id, AgentTaskStatus.Working, null, task.CallerAgentId, null));
  }

  public async Task<Result<TaskResultOutcome>> ReceiveResultAsync(TaskResult result, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default)
  {
    // Order (decision A28): authenticate, taskId shape, 404, 403, payload rules (400), state (409), compare-and-set (409).
    if (sender.AgentId is not { } senderId)
      return Unauthenticated<TaskResultOutcome>();

    if (!Guid.TryParseExact(result.TaskId, "D", out var taskId))
      return Result<TaskResultOutcome>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.",
        [new ErrorDetail("/payload/taskId", "taskId must be a UUID.")]);

    var task = await unitOfWork.TaskRepository.GetByIdAsync(taskId, trackChanges: false, cancellationToken);
    if (task is null)
      return Result<TaskResultOutcome>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The task does not exist.");
    if (task.TargetAgentId != senderId)
      return Result<TaskResultOutcome>.Failure(ResultStatus.Forbidden, ErrorCodes.TaskAccessForbidden, "Only the target agent can report the result of a task.");

    var errors = ValidateResult(result, taskOptions.Value.MaxOutputBytes);
    if (errors.Count > 0)
      return Result<TaskResultOutcome>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.", errors);

    if (task.Status != AgentTaskStatus.Working)
      return Result<TaskResultOutcome>.Failure(ResultStatus.Conflict, ErrorCodes.TaskStateConflict, "The task is not in the working state.");

    var completed = result.Status == "completed";
    var next = completed ? AgentTaskStatus.Completed : AgentTaskStatus.Failed;
    var output = completed ? result.Output : null;
    var error = completed ? null : result.Error!.ToTaskError();

    // Two concurrent results for the same task: the compare-and-set lets exactly one win (decision A39).
    if (!await TransitionAsync(taskId, AgentTaskStatus.Working, next, timeProvider.GetUtcNow(), output, error, cancellationToken))
      return Result<TaskResultOutcome>.Failure(ResultStatus.Conflict, ErrorCodes.TaskStateConflict, "The task is not in the working state.");

    var forwarded = new TaskResult(taskId.ToString(), next.ToWire(), output, error?.ToPayload());
    return Result<TaskResultOutcome>.Success(new TaskResultOutcome(taskId, next, task.CallerAgentId, forwarded));
  }

  public async Task<Result<TaskStatusEventOutcome>> ReceiveStatusEventAsync(TaskStatusEvent statusEvent, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default)
  {
    if (sender.AgentId is not { } senderId)
      return Unauthenticated<TaskStatusEventOutcome>();

    if (!Guid.TryParseExact(statusEvent.TaskId, "D", out var taskId))
      return Result<TaskStatusEventOutcome>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.",
        [new ErrorDetail("/payload/taskId", "taskId must be a UUID.")]);

    var task = await unitOfWork.TaskRepository.GetByIdAsync(taskId, trackChanges: false, cancellationToken);
    if (task is null)
      return Result<TaskStatusEventOutcome>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The task does not exist.");
    if (task.TargetAgentId != senderId)
      return Result<TaskStatusEventOutcome>.Failure(ResultStatus.Forbidden, ErrorCodes.TaskAccessForbidden, "Only the target agent can report the status of a task.");

    if (statusEvent.Status != "working")
      return Result<TaskStatusEventOutcome>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.",
        [new ErrorDetail("/payload/status", "Only the status 'working' is accepted.")]);

    // A no-op in Week 6: dispatch already committed "working" before the invocation was sent.
    if (task.Status != AgentTaskStatus.Working)
      return Result<TaskStatusEventOutcome>.Failure(ResultStatus.Conflict, ErrorCodes.TaskStateConflict, "The task is not in the working state.");

    return Result<TaskStatusEventOutcome>.Success(new TaskStatusEventOutcome(taskId, task.Status));
  }

  // ---------------------------------------------------------------------------------------------------------------
  // A-05: query
  // ---------------------------------------------------------------------------------------------------------------

  public async Task<Result<TaskQueryResult>> GetAsync(Guid taskId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default)
  {
    if (caller.AgentId is not { } callerId)
      return Unauthenticated<TaskQueryResult>();

    // 404 before 403 (decision A33). PostgreSQL only in Week 6.
    var task = await unitOfWork.TaskRepository.GetByIdAsync(taskId, trackChanges: false, cancellationToken);
    if (task is null)
      return Result<TaskQueryResult>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The task does not exist.");
    if (task.CallerAgentId != callerId && task.TargetAgentId != callerId)
      return Result<TaskQueryResult>.Failure(ResultStatus.Forbidden, ErrorCodes.TaskAccessForbidden, "Only the caller or the target agent of a task can read it.");

    return Result<TaskQueryResult>.Success(task.ToQueryResult());
  }

  // ---------------------------------------------------------------------------------------------------------------
  // Shared internals
  // ---------------------------------------------------------------------------------------------------------------

  /// <summary>
  /// The only way a status changes: compare-and-set, one history row and one task.status_changed audit event in the same
  /// transaction (decision A27). Returns false when the task was not in the expected status (a concurrent change won).
  /// </summary>
  private async Task<bool> TransitionAsync(
    Guid taskId,
    AgentTaskStatus expected,
    AgentTaskStatus next,
    DateTimeOffset now,
    JsonElement? output,
    TaskError? error,
    CancellationToken cancellationToken)
  {
    if (!expected.CanTransitionTo(next))
      throw new InvalidOperationException($"The transition {expected} to {next} is not part of the task lifecycle.");

    await using var transaction = await unitOfWork.StartTransaction(cancellationToken);

    if (!await unitOfWork.TaskRepository.TryTransitionAsync(taskId, expected, next, now, output, error, cancellationToken))
    {
      await transaction.RollbackTransactionAsync(cancellationToken);
      return false;
    }

    var details = JsonSerializer.SerializeToElement(new { fromStatus = expected.ToWire(), toStatus = next.ToWire() }, EnvelopeJson.Options);
    await unitOfWork.TaskRepository.AddHistoryAsync(TaskStatusHistory.Create(taskId, expected, next, now), cancellationToken);
    await unitOfWork.AuditEventRepository.AddAsync(AuditEvent.Create(AuditEventTypes.TaskStatusChanged, now, null, taskId, details), cancellationToken);
    await unitOfWork.SaveChangesAsync(cancellationToken);
    await transaction.CommitTransactionAsync(cancellationToken);
    return true;
  }

  private async Task<Result<TaskDispatchOutcome>> FailWithTargetUnavailableAsync(AgentTask task, AgentTaskStatus expected, CancellationToken cancellationToken)
  {
    var error = new TaskError(ErrorCodes.TargetUnavailable, "The target agent is not connected.", null, true);

    if (!await TransitionAsync(task.Id, expected, AgentTaskStatus.Failed, timeProvider.GetUtcNow(), null, error, cancellationToken))
      return await CurrentOutcomeAsync(task.Id, cancellationToken);

    var payload = error.ToPayload();
    var result = new TaskResult(task.Id.ToString(), AgentTaskStatus.Failed.ToWire(), null, payload);
    return Result<TaskDispatchOutcome>.Success(new TaskDispatchOutcome(task.Id, AgentTaskStatus.Failed, payload, task.CallerAgentId, result));
  }

  // A compare-and-set lost: someone else changed the task. Report what is stored and push nothing.
  private async Task<Result<TaskDispatchOutcome>> CurrentOutcomeAsync(Guid taskId, CancellationToken cancellationToken)
  {
    var current = await unitOfWork.TaskRepository.GetByIdAsync(taskId, trackChanges: false, cancellationToken);
    if (current is null)
      return Result<TaskDispatchOutcome>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The task does not exist.");

    return Result<TaskDispatchOutcome>.Success(new TaskDispatchOutcome(current.Id, current.Status, current.Error?.ToPayload(), current.CallerAgentId, null));
  }

  /// <summary>Writes policy.rejected in its own SaveChanges; a failure to audit never changes the error returned to the caller.</summary>
  private async Task AuditRejectionAsync(Guid callerId, Result failure, CancellationToken cancellationToken)
  {
    try
    {
      var details = JsonSerializer.SerializeToElement(new { errorCode = failure.ErrorCode }, EnvelopeJson.Options);
      await unitOfWork.AuditEventRepository.AddAsync(
        AuditEvent.Create(AuditEventTypes.PolicyRejected, timeProvider.GetUtcNow(), callerId, null, details), cancellationToken);
      await unitOfWork.SaveChangesAsync(cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not write the policy.rejected audit event for agent {AgentId}.", callerId);
    }
  }

  private static List<ErrorDetail> ValidateResult(TaskResult result, int maxOutputBytes)
  {
    var errors = new List<ErrorDetail>();

    switch (result.Status)
    {
      case "completed":
        if (result.Output is not { ValueKind: JsonValueKind.Object } output)
          errors.Add(new ErrorDetail("/payload/output", "output is required and must be an object for a completed result."));
        else if (Encoding.UTF8.GetByteCount(output.GetRawText()) > maxOutputBytes)
          errors.Add(new ErrorDetail("/payload/output", $"output must not exceed {maxOutputBytes} bytes."));
        if (result.Error is not null)
          errors.Add(new ErrorDetail("/payload/error", "error must be null for a completed result."));
        break;

      case "failed":
        if (result.Error is null)
          errors.Add(new ErrorDetail("/payload/error", "error is required for a failed result."));
        else
        {
          if (string.IsNullOrWhiteSpace(result.Error.Code))
            errors.Add(new ErrorDetail("/payload/error/code", "code must not be empty."));
          if (string.IsNullOrWhiteSpace(result.Error.Message))
            errors.Add(new ErrorDetail("/payload/error/message", "message must not be empty."));
        }
        if (result.Output is not null)
          errors.Add(new ErrorDetail("/payload/output", "output must be null for a failed result."));
        break;

      default:
        errors.Add(new ErrorDetail("/payload/status", "status must be 'completed' or 'failed'."));
        break;
    }

    return errors;
  }
}

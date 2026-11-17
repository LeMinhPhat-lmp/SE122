using System.Globalization;
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
using Application.Interfaces.Validation;
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
  IRateLimiter rateLimiter,
  IInputSchemaValidator inputSchemaValidator,
  IConnectionRegistry connectionRegistry,
  IMessageRelay messageRelay,
  ITaskRuntimeStateStore runtimeStateStore,
  IOptions<TaskOptions> taskOptions,
  IOptions<AersPolicyOptions> policyOptions,
  IOptions<InstanceOptions> instanceOptions,
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
    // Order (A53, ADR-W8-01): authenticate, shape (400), caller active, rate limit, target exists, parent rules, scope policy,
    // depth, cycle, create. Every rejection from the rate limit onward is audited as policy.rejected and creates no Task (A20).
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

    // ADR-W8-01 (a2, b1): the limiter runs early, so every task.create that passed shape and caller checks consumes one unit of
    // quota, including requests rejected by a later stage (target, parent, scope, depth, cycle). A denied request still
    // increments the counter inside the limiter. Redis failure is fail-closed.
    RateLimitDecision decision;
    try
    {
      decision = await rateLimiter.TryAcquireAsync(callerId, cancellationToken);
    }
    catch (StateStoreUnavailableException ex)
    {
      logger.LogWarning(ex, "The rate limiter is unavailable; task.create of agent {AgentId} is rejected (fail-closed).", callerId);
      var unavailable = Result.Failure(ResultStatus.InternalError, ErrorCodes.InternalError, "A required runtime service is unavailable. Try again later.", null, true);
      await AuditRejectionAsync(callerId, unavailable, cancellationToken);
      return unavailable.ToFailure<AgentTask>();
    }

    if (!decision.Allowed)
    {
      var limitDetails = JsonSerializer.SerializeToElement(
        new { limit = decision.Limit, remaining = 0, resetAt = decision.ResetAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) },
        EnvelopeJson.Options);
      var limited = Result.Failure(ResultStatus.TooManyRequests, ErrorCodes.RateLimited, "The rate limit was exceeded.", limitDetails, true);
      await AuditRejectionAsync(callerId, limited, cancellationToken);
      return limited.ToFailure<AgentTask>();
    }

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

    // Ancestry (decision A26). It is derived from the parent row, never from the client, and computed exactly once.
    var now = timeProvider.GetUtcNow();
    var taskId = Guid.NewGuid();
    var rootTaskId = parent?.RootTaskId ?? taskId;
    var depth = parent is null ? 0 : parent.Depth + 1;
    List<Guid> ancestorChain = parent is null ? new() { callerId } : new(parent.AncestorChain) { parent.TargetAgentId };

    // Depth first, then cycle (A54, A55). A rejection here has already consumed quota. The chain itself is never returned.
    var recursion = policyEvaluator.EvaluateRecursion(targetId, depth, ancestorChain);
    if (!recursion.IsSuccess)
    {
      recursion.StructuredDetails = recursion.ErrorCode == ErrorCodes.DepthExceeded
        ? JsonSerializer.SerializeToElement(new { maxDepth = policyOptions.Value.MaxRecursionDepth, depth }, EnvelopeJson.Options)
        : JsonSerializer.SerializeToElement(new { rootTaskId }, EnvelopeJson.Options);
      recursion.Retryable = false;
      await AuditRejectionAsync(callerId, recursion, cancellationToken);
      return recursion.ToFailure<AgentTask>();
    }

    // A47 (ADR-W7-04): a child never outlives its parent. If the parent is already past its deadline the child is still created
    // with that deadline and the next worker tick expires it.
    var deadlineAt = now.AddSeconds(options.DefaultTaskTimeoutSeconds);
    if (parent is not null && parent.DeadlineAt < deadlineAt)
      deadlineAt = parent.DeadlineAt;

    var task = AgentTask.Create(
      taskId,
      callerId,
      targetId,
      request.Capability!,
      request.Input,
      parentTaskId,
      rootTaskId: rootTaskId,
      depth: depth,
      ancestorChain: ancestorChain,
      deadlineAt: deadlineAt,
      createdAt: now);

    // ADR-W8-02: an agent that holds a working task and starts a root task is suspicious (it may have dropped parentTaskId).
    // Nothing is rejected; the audit event only carries the hint. The extra query runs for root tasks only.
    var auditDetails = new Dictionary<string, object> { ["targetAgentId"] = targetId, ["capability"] = request.Capability! };
    if (parentTaskId is null && await unitOfWork.TaskRepository.CountWorkingAsTargetAsync(callerId, cancellationToken) > 0)
      auditDetails["orphanSuspect"] = true;

    // One SaveChanges is one transaction: the task row and its audit event commit together. No Task exists on any failure above.
    var details = JsonSerializer.SerializeToElement(auditDetails, EnvelopeJson.Options);
    await unitOfWork.TaskRepository.AddAsync(task, cancellationToken);
    await unitOfWork.AuditEventRepository.AddAsync(AuditEvent.Create(AuditEventTypes.TaskCreated, now, callerId, taskId, details), cancellationToken);
    await unitOfWork.SaveChangesAsync(cancellationToken);

    await WriteRuntimeStateAsync(taskId, AgentTaskStatus.Submitted, now, deadlineAt, null, cancellationToken);

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
    if (!await TransitionAsync(task.Id, AgentTaskStatus.Submitted, AgentTaskStatus.Working, timeProvider.GetUtcNow(), null, null, task.DeadlineAt, connection.ConnectionId, cancellationToken))
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

    // ADR-W8-03: a connection can outlive its credential, so every message re-checks that the sender is still an active agent.
    if (await unitOfWork.AgentRepository.GetByIdAsync(senderId, trackChanges: false, cancellationToken) is null)
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

    // ADR-W9-02: the output is checked against the capability outputSchema for a warning only; the task completes either way.
    if (completed)
      await WarnOnOutputViolationsAsync(task, output!.Value, cancellationToken);

    // Two concurrent results for the same task: the compare-and-set lets exactly one win (decision A39).
    if (!await TransitionAsync(taskId, AgentTaskStatus.Working, next, timeProvider.GetUtcNow(), output, error, task.DeadlineAt, null, cancellationToken))
      return Result<TaskResultOutcome>.Failure(ResultStatus.Conflict, ErrorCodes.TaskStateConflict, "The task is not in the working state.");

    var forwarded = new TaskResult(taskId.ToString(), next.ToWire(), output, error?.ToPayload());
    return Result<TaskResultOutcome>.Success(new TaskResultOutcome(taskId, next, task.CallerAgentId, forwarded));
  }

  public async Task<Result<TaskStatusEventOutcome>> ReceiveStatusEventAsync(TaskStatusEvent statusEvent, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default)
  {
    if (sender.AgentId is not { } senderId)
      return Unauthenticated<TaskStatusEventOutcome>();

    if (await unitOfWork.AgentRepository.GetByIdAsync(senderId, trackChanges: false, cancellationToken) is null)
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
  // Week 7: acknowledgement, expiry, disconnected targets
  // ---------------------------------------------------------------------------------------------------------------

  public async Task<Result<TaskAckOutcome>> AcknowledgeAsync(TaskAck ack, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default)
  {
    if (sender.AgentId is not { } senderId)
      return Unauthenticated<TaskAckOutcome>();

    if (await unitOfWork.AgentRepository.GetByIdAsync(senderId, trackChanges: false, cancellationToken) is null)
      return Unauthenticated<TaskAckOutcome>();

    if (!Guid.TryParseExact(ack.TaskId, "D", out var taskId))
      return Result<TaskAckOutcome>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.",
        [new ErrorDetail("/payload/taskId", "taskId must be a UUID.")]);

    var task = await unitOfWork.TaskRepository.GetByIdAsync(taskId, trackChanges: false, cancellationToken);
    if (task is null)
      return Result<TaskAckOutcome>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The task does not exist.");
    if (task.CallerAgentId != senderId)
      return Result<TaskAckOutcome>.Failure(ResultStatus.Forbidden, ErrorCodes.TaskAccessForbidden, "Only the caller agent can acknowledge the result of a task.");
    if (!task.Status.IsTerminal())
      return Result<TaskAckOutcome>.Failure(ResultStatus.Conflict, ErrorCodes.TaskStateConflict, "The task has no result to acknowledge yet.");

    // Idempotent: the conditional UPDATE changes nothing when acknowledged_at is already set, and that is still a success.
    await unitOfWork.TaskRepository.TryAcknowledgeAsync(taskId, senderId, timeProvider.GetUtcNow(), cancellationToken);
    return Result<TaskAckOutcome>.Success(new TaskAckOutcome(taskId, task.Status));
  }

  public async Task<Result<TaskExpiryOutcome>> ExpireAsync(Guid taskId, CancellationToken cancellationToken = default)
  {
    var task = await unitOfWork.TaskRepository.GetByIdAsync(taskId, trackChanges: false, cancellationToken);
    if (task is null)
      return Result<TaskExpiryOutcome>.Failure(ResultStatus.NotFound, ErrorCodes.TaskNotFound, "The task does not exist.");

    var now = timeProvider.GetUtcNow();
    if (task.Status.IsTerminal() || task.DeadlineAt >= now)
      return Result<TaskExpiryOutcome>.Success(new TaskExpiryOutcome(taskId, false, task.CallerAgentId, null));

    var error = new TaskError(ErrorCodes.TaskTimeout, "The task did not complete before its deadline.", null, false);

    // Compare-and-set from the status that was just read: a result, another instance or another worker tick may have won.
    if (!await TransitionAsync(taskId, task.Status, AgentTaskStatus.Failed, now, null, error, task.DeadlineAt, null, cancellationToken))
      return Result<TaskExpiryOutcome>.Success(new TaskExpiryOutcome(taskId, false, task.CallerAgentId, null));

    var result = new TaskResult(taskId.ToString(), AgentTaskStatus.Failed.ToWire(), null, error.ToPayload());
    return Result<TaskExpiryOutcome>.Success(new TaskExpiryOutcome(taskId, true, task.CallerAgentId, result));
  }

  public async Task<Result<DisconnectFailureBatch>> FailTasksOfDisconnectedTargetAsync(Guid targetAgentId, int limit, CancellationToken cancellationToken = default)
  {
    var working = await unitOfWork.TaskRepository.ListWorkingByTargetAsync(targetAgentId, limit, cancellationToken);
    var error = new TaskError(ErrorCodes.TargetUnavailable, "The target agent disconnected and did not reconnect within the grace period.", null, true);
    var failed = new List<TaskExpiryOutcome>();

    foreach (var item in working)
    {
      // A result that arrived first wins the compare-and-set; this task is then simply skipped.
      if (!await TransitionAsync(item.Id, AgentTaskStatus.Working, AgentTaskStatus.Failed, timeProvider.GetUtcNow(), null, error, item.DeadlineAt, null, cancellationToken))
        continue;

      var result = new TaskResult(item.Id.ToString(), AgentTaskStatus.Failed.ToWire(), null, error.ToPayload());
      failed.Add(new TaskExpiryOutcome(item.Id, true, item.CallerAgentId, result));
    }

    return Result<DisconnectFailureBatch>.Success(new DisconnectFailureBatch(working.Count, failed));
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
    DateTimeOffset deadlineAt,
    string? connectionId,
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

    // After the commit and only for a transition that won the compare-and-set (A43, ADR-W7-01).
    await WriteRuntimeStateAsync(taskId, next, now, deadlineAt, connectionId, cancellationToken);
    return true;
  }

  /// <summary>Best effort (A43, A44): a Redis problem is logged with the task id only and never changes the outcome that was already committed.</summary>
  private async Task WriteRuntimeStateAsync(Guid taskId, AgentTaskStatus status, DateTimeOffset now, DateTimeOffset deadlineAt, string? connectionId, CancellationToken cancellationToken)
  {
    var options = taskOptions.Value;
    var ttl = status.IsTerminal()
      ? TimeSpan.FromSeconds(options.RuntimeStateTerminalTtlSeconds)
      : (deadlineAt > now ? deadlineAt - now : TimeSpan.Zero) + TimeSpan.FromSeconds(options.RuntimeStateGraceSeconds);

    try
    {
      await runtimeStateStore.SetAsync(new TaskRuntimeState(taskId, status, now, connectionId, instanceOptions.Value.Id, deadlineAt), ttl, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not write the runtime state of task {TaskId}.", taskId);
    }
  }

  /// <summary>ADR-W9-02: never changes the result. A missing capability or a failing check only produces a log line without the output.</summary>
  private async Task WarnOnOutputViolationsAsync(AgentTask task, JsonElement output, CancellationToken cancellationToken)
  {
    try
    {
      var target = await unitOfWork.AgentRepository.GetWithCapabilitiesAsync(task.TargetAgentId, cancellationToken);
      var capability = target?.Capabilities.FirstOrDefault(candidate =>
        !candidate.IsDeleted && string.Equals(candidate.Name, task.CapabilityName, StringComparison.Ordinal));
      if (capability is null)
      {
        logger.LogInformation("Output check skipped for task {TaskId}: capability {Capability} was not found.", task.Id, task.CapabilityName);
        return;
      }

      var violations = inputSchemaValidator.Validate(capability.OutputSchemaJson, output);
      if (violations.Count > 0)
        logger.LogWarning("The output of task {TaskId} violates the outputSchema of capability {Capability} ({ViolationCount} violations).",
          task.Id, task.CapabilityName, violations.Count);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "The output check of task {TaskId} could not run; the task is completed anyway.", task.Id);
    }
  }

  private async Task<Result<TaskDispatchOutcome>> FailWithTargetUnavailableAsync(AgentTask task, AgentTaskStatus expected, CancellationToken cancellationToken)
  {
    var error = new TaskError(ErrorCodes.TargetUnavailable, "The target agent is not connected.", null, true);

    if (!await TransitionAsync(task.Id, expected, AgentTaskStatus.Failed, timeProvider.GetUtcNow(), null, error, task.DeadlineAt, null, cancellationToken))
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

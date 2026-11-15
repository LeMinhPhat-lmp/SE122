using System.Text.Json;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;
using Application.Exceptions;
using Application.Models;
using Domain.Constants;
using Domain.Enums;
using Xunit;

namespace AERS.UnitTests;

public sealed class TaskServiceWeek7Tests
{
  private static CreateTaskRequest Request(Guid target, Guid? parent = null) =>
    new(target.ToString(), "echo", TaskFixture.Json("{}"), parent?.ToString());

  // ---------------------------------------------------------------- runtime state (A-01)

  [Fact]
  public async Task Create_writes_submitted_state_with_deadline_and_ttl()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();

    var task = await f.CreateAsync(a, b, working: false);

    var write = Assert.Single(f.State.Writes);
    Assert.Equal(AgentTaskStatus.Submitted, write.State.Status);
    Assert.Equal(task.DeadlineAt, write.State.DeadlineAt);
    Assert.Equal(f.Time.GetUtcNow().AddSeconds(300), task.DeadlineAt);
    Assert.Equal(TimeSpan.FromSeconds(300 + 60), write.Ttl);
    Assert.Equal("test-1", write.State.InstanceId);
    Assert.Null(write.State.ConnectionId);
  }

  [Fact]
  public async Task Dispatch_writes_working_state_with_connection_id_after_commit()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    f.Connections.Connections[b.Id] = new ConnectionInfo("conn-b", "test-1");
    var task = await f.CreateAsync(a, b, working: false);

    var dispatched = await f.Service.DispatchAsync(task);

    Assert.True(dispatched.IsSuccess);
    var write = f.State.Writes.Last();
    Assert.Equal(AgentTaskStatus.Working, write.State.Status);
    Assert.Equal("conn-b", write.State.ConnectionId);
    Assert.Equal(AgentTaskStatus.Working, write.StoredStatus); // the row already had the new status when the state was written
    Assert.Equal(TimeSpan.FromSeconds(300 + 60), write.Ttl);
  }

  [Fact]
  public async Task Terminal_state_uses_the_terminal_ttl()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var task = await f.CreateAsync(a, b);

    var received = await f.Service.ReceiveResultAsync(
      new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));

    Assert.True(received.IsSuccess);
    var write = f.State.Writes.Last();
    Assert.Equal(AgentTaskStatus.Completed, write.State.Status);
    Assert.Equal(TimeSpan.FromSeconds(60), write.Ttl);
  }

  [Fact]
  public async Task Lost_compare_and_set_writes_no_state()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    f.Connections.Connections[b.Id] = new ConnectionInfo("conn-b", "test-1");
    var task = await f.CreateAsync(a, b, working: false);
    var before = f.State.Writes.Count;

    f.Tasks.FailNextTransition = true;
    var dispatched = await f.Service.DispatchAsync(task);

    Assert.True(dispatched.IsSuccess);
    Assert.Equal(before, f.State.Writes.Count);
  }

  [Fact]
  public async Task A_failing_state_store_never_changes_a_result()
  {
    var f = new TaskFixture();
    f.State.FailWith = new StateStoreUnavailableException(new InvalidOperationException("redis down"));
    var a = f.AddAgent();
    var b = f.AddAgent();
    f.Connections.Connections[b.Id] = new ConnectionInfo("conn-b", "test-1");

    var task = await f.CreateAsync(a, b, working: false);
    var dispatched = await f.Service.DispatchAsync(task);
    var received = await f.Service.ReceiveResultAsync(
      new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));

    Assert.True(dispatched.IsSuccess);
    Assert.True(received.IsSuccess);
    Assert.Equal(AgentTaskStatus.Completed, task.Status);
    Assert.Equal(3, f.Log.Warnings.Count());
    Assert.All(f.Log.Warnings, message => Assert.Contains(task.Id.ToString(), message));
  }

  // ---------------------------------------------------------------- expiry and child deadline (A-02)

  [Fact]
  public async Task Expire_fails_a_working_task_with_timeout_history_audit_and_result()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var task = await f.CreateAsync(a, b);
    f.Time.Advance(TimeSpan.FromSeconds(301));

    var expired = await f.Service.ExpireAsync(task.Id);

    Assert.True(expired.IsSuccess);
    Assert.True(expired.Data!.Expired);
    Assert.Equal(a.Id, expired.Data.CallerAgentId);
    Assert.Equal("failed", expired.Data.ResultToPush!.Status);
    Assert.Equal(ErrorCodes.TaskTimeout, expired.Data.ResultToPush.Error!.Code);
    Assert.False(expired.Data.ResultToPush.Error.Retryable);
    Assert.Equal(AgentTaskStatus.Failed, task.Status);
    var history = Assert.Single(f.Tasks.History);
    Assert.Equal(AgentTaskStatus.Working, history.FromStatus);
    Assert.Equal(AgentTaskStatus.Failed, history.ToStatus);
    Assert.Single(f.Audit.Events, e => e.EventType == AuditEventTypes.TaskStatusChanged);
  }

  [Fact]
  public async Task Expire_also_fails_a_submitted_task()
  {
    var f = new TaskFixture();
    var task = await f.CreateAsync(f.AddAgent(), f.AddAgent(), working: false);
    f.Time.Advance(TimeSpan.FromSeconds(301));

    var expired = await f.Service.ExpireAsync(task.Id);

    Assert.True(expired.Data!.Expired);
    Assert.Equal(AgentTaskStatus.Submitted, f.Tasks.History.Single().FromStatus);
  }

  [Fact]
  public async Task Expire_changes_nothing_when_not_due_terminal_or_lost()
  {
    var f = new TaskFixture();
    var task = await f.CreateAsync(f.AddAgent(), f.AddAgent());

    Assert.False((await f.Service.ExpireAsync(task.Id)).Data!.Expired); // not due yet
    Assert.Empty(f.Tasks.History);

    f.Time.Advance(TimeSpan.FromSeconds(301));
    f.Tasks.FailNextTransition = true;
    Assert.False((await f.Service.ExpireAsync(task.Id)).Data!.Expired); // compare-and-set lost
    Assert.Empty(f.Tasks.History);

    Assert.True((await f.Service.ExpireAsync(task.Id)).Data!.Expired);
    Assert.False((await f.Service.ExpireAsync(task.Id)).Data!.Expired); // already terminal
    Assert.Single(f.Tasks.History);

    var unknown = await f.Service.ExpireAsync(Guid.NewGuid());
    Assert.Equal(ErrorCodes.TaskNotFound, unknown.ErrorCode);
  }

  [Fact]
  public async Task Two_concurrent_expiries_produce_exactly_one_transition()
  {
    var f = new TaskFixture();
    var task = await f.CreateAsync(f.AddAgent(), f.AddAgent());
    f.Time.Advance(TimeSpan.FromSeconds(301));

    var outcomes = await Task.WhenAll(f.Service.ExpireAsync(task.Id), f.Service.ExpireAsync(task.Id));

    Assert.Equal(1, outcomes.Count(outcome => outcome.Data!.Expired));
    Assert.Single(f.Tasks.History);
  }

  [Fact]
  public async Task Late_result_after_timeout_is_a_state_conflict_and_keeps_the_timeout_error()
  {
    var f = new TaskFixture();
    var b = f.AddAgent();
    var task = await f.CreateAsync(f.AddAgent(), b);
    f.Time.Advance(TimeSpan.FromSeconds(301));
    await f.Service.ExpireAsync(task.Id);

    var late = await f.Service.ReceiveResultAsync(
      new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));

    Assert.Equal(ErrorCodes.TaskStateConflict, late.ErrorCode);
    Assert.Equal(ErrorCodes.TaskTimeout, task.Error!.Code);
  }

  [Fact]
  public async Task Child_deadline_is_clamped_to_the_parent_and_a_root_is_not()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    var root = await f.CreateAsync(a, b);
    f.Time.Advance(TimeSpan.FromSeconds(290)); // the parent has 10 seconds left

    var child = await f.CreateAsync(b, c, root);

    Assert.Equal(root.DeadlineAt, child.DeadlineAt);
    Assert.True(child.DeadlineAt < f.Time.GetUtcNow().AddSeconds(300));

    var freshRoot = await f.CreateAsync(a, c);
    Assert.Equal(f.Time.GetUtcNow().AddSeconds(300), freshRoot.DeadlineAt);
  }

  [Fact]
  public async Task Child_of_an_overdue_parent_is_still_created_with_the_parent_deadline()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    var root = await f.CreateAsync(a, b);
    f.Time.Advance(TimeSpan.FromSeconds(400));

    var child = await f.CreateAsync(b, c, root);

    Assert.Equal(root.DeadlineAt, child.DeadlineAt);
  }

  // ---------------------------------------------------------------- disconnected target (ADR-W7-03)

  [Fact]
  public async Task Disconnected_target_fails_only_its_working_tasks_and_skips_lost_races()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    var t1 = await f.CreateAsync(a, b);
    var t2 = await f.CreateAsync(c, b);
    var other = await f.CreateAsync(a, c);

    var batch = await f.Service.FailTasksOfDisconnectedTargetAsync(b.Id, 100);

    Assert.Equal(2, batch.Data!.Examined);
    Assert.Equal(2, batch.Data.Failed.Count);
    Assert.All(batch.Data.Failed, outcome => Assert.Equal(ErrorCodes.TargetUnavailable, outcome.ResultToPush!.Error!.Code));
    Assert.Equal(AgentTaskStatus.Failed, t1.Status);
    Assert.Equal(AgentTaskStatus.Failed, t2.Status);
    Assert.Equal(AgentTaskStatus.Working, other.Status);

    var f2 = new TaskFixture();
    var b2 = f2.AddAgent();
    await f2.CreateAsync(f2.AddAgent(), b2);
    f2.Tasks.FailNextTransition = true;
    var raced = await f2.Service.FailTasksOfDisconnectedTargetAsync(b2.Id, 100);
    Assert.Equal(1, raced.Data!.Examined);
    Assert.Empty(raced.Data.Failed);
  }

  // ---------------------------------------------------------------- acknowledgement (ADR-W7-02)

  [Fact]
  public async Task Ack_sets_acknowledged_at_once_and_repeated_acks_are_accepted()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var task = await f.CreateAsync(a, b);
    await f.Service.ReceiveResultAsync(new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));

    var first = await f.Service.AcknowledgeAsync(new TaskAck(task.Id.ToString()), TaskFixture.As(a));
    var stamped = task.AcknowledgedAt;
    f.Time.Advance(TimeSpan.FromSeconds(5));
    var second = await f.Service.AcknowledgeAsync(new TaskAck(task.Id.ToString()), TaskFixture.As(a));

    Assert.True(first.IsSuccess);
    Assert.True(second.IsSuccess);
    Assert.NotNull(stamped);
    Assert.Equal(stamped, task.AcknowledgedAt);
    Assert.Equal(AgentTaskStatus.Completed, second.Data!.Status);
  }

  [Fact]
  public async Task Ack_rejections_use_the_documented_codes()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var task = await f.CreateAsync(a, b);

    Assert.Equal(ErrorCodes.TaskStateConflict, (await f.Service.AcknowledgeAsync(new TaskAck(task.Id.ToString()), TaskFixture.As(a))).ErrorCode);
    Assert.Equal(ErrorCodes.TaskNotFound, (await f.Service.AcknowledgeAsync(new TaskAck(Guid.NewGuid().ToString()), TaskFixture.As(a))).ErrorCode);
    Assert.Equal(ErrorCodes.TaskAccessForbidden, (await f.Service.AcknowledgeAsync(new TaskAck(task.Id.ToString()), TaskFixture.As(b))).ErrorCode);
    Assert.Equal(ErrorCodes.ValidationError, (await f.Service.AcknowledgeAsync(new TaskAck("nope"), TaskFixture.As(a))).ErrorCode);
  }

  // ---------------------------------------------------------------- deregistered sender (ADR-W8-03)

  [Fact]
  public async Task Deregistered_sender_is_rejected_for_result_status_event_and_ack()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var task = await f.CreateAsync(a, b);
    b.DeletedAt = f.Time.GetUtcNow();
    a.DeletedAt = f.Time.GetUtcNow();

    var result = await f.Service.ReceiveResultAsync(new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));
    var status = await f.Service.ReceiveStatusEventAsync(new TaskStatusEvent(task.Id.ToString(), "working"), TaskFixture.As(b));
    var ack = await f.Service.AcknowledgeAsync(new TaskAck(task.Id.ToString()), TaskFixture.As(a));

    Assert.Equal(ErrorCodes.AuthenticationFailed, result.ErrorCode);
    Assert.Equal(ErrorCodes.AuthenticationFailed, status.ErrorCode);
    Assert.Equal(ErrorCodes.AuthenticationFailed, ack.ErrorCode);
    Assert.Equal(AgentTaskStatus.Working, task.Status);
  }

  // ---------------------------------------------------------------- output check (ADR-W9-02)

  [Fact]
  public async Task Output_violation_logs_one_warning_without_the_output_and_still_completes()
  {
    var f = new TaskFixture();
    var b = f.AddAgent();
    var task = await f.CreateAsync(f.AddAgent(), b);
    f.Validator.Violations.Add(new ErrorDetail("/score", "score is required."));

    var received = await f.Service.ReceiveResultAsync(
      new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{\"secret\":\"top-secret-value\"}"), null), TaskFixture.As(b));

    Assert.True(received.IsSuccess);
    Assert.Equal(AgentTaskStatus.Completed, task.Status);
    var warning = Assert.Single(f.Log.Warnings);
    Assert.Contains(task.Id.ToString(), warning);
    Assert.Contains("echo", warning);
    Assert.DoesNotContain("top-secret-value", warning);
  }

  [Fact]
  public async Task Valid_output_logs_no_warning_and_a_throwing_validator_does_not_fail_the_task()
  {
    var f = new TaskFixture();
    var b = f.AddAgent();
    var task = await f.CreateAsync(f.AddAgent(), b);

    await f.Service.ReceiveResultAsync(new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));
    Assert.Empty(f.Log.Warnings);

    var second = await f.CreateAsync(f.AddAgent(), b);
    f.Validator.FailWith = new InvalidOperationException("boom");
    var received = await f.Service.ReceiveResultAsync(new TaskResult(second.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));

    Assert.True(received.IsSuccess);
    Assert.Equal(AgentTaskStatus.Completed, second.Status);
  }

  // ---------------------------------------------------------------- rate limit (ADR-W8-01)

  [Fact]
  public async Task Rate_limited_request_creates_no_task_carries_details_and_does_not_load_the_target()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var resetAt = f.Time.GetUtcNow().AddSeconds(30);
    f.Limiter.Decide = () => new RateLimitDecision(false, 5, 0, resetAt);

    var result = await f.Service.CreateAsync(Request(b.Id), TaskFixture.As(a));

    Assert.Equal(ErrorCodes.RateLimited, result.ErrorCode);
    Assert.Equal(ResultStatus.TooManyRequests, result.ResultStatus);
    Assert.True(result.Retryable);
    var details = result.StructuredDetails!.Value;
    Assert.Equal(5, details.GetProperty("limit").GetInt32());
    Assert.Equal(0, details.GetProperty("remaining").GetInt32());
    Assert.Equal(resetAt.UtcDateTime, DateTimeOffset.Parse(details.GetProperty("resetAt").GetString()!).UtcDateTime);
    Assert.Empty(f.Tasks.Tasks);
    Assert.Equal(0, f.Agents.CapabilityLookups);
    Assert.Equal(ErrorCodes.RateLimited, RejectionCode(f));
  }

  [Fact]
  public async Task Limiter_outage_is_fail_closed_with_a_retryable_internal_error()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    f.Limiter.FailWith = new StateStoreUnavailableException(new InvalidOperationException("redis down"));

    var result = await f.Service.CreateAsync(Request(b.Id), TaskFixture.As(a));

    Assert.Equal(ErrorCodes.InternalError, result.ErrorCode);
    Assert.True(result.Retryable);
    Assert.Empty(f.Tasks.Tasks);
    Assert.Equal(ErrorCodes.InternalError, RejectionCode(f));
    Assert.Single(f.Log.Warnings);
  }

  [Fact]
  public async Task Quota_is_consumed_after_shape_and_caller_checks_even_when_a_later_stage_rejects()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();

    var badShape = await f.Service.CreateAsync(new CreateTaskRequest("not-a-guid", "echo", TaskFixture.Json("{}")), TaskFixture.As(a));
    Assert.Equal(ErrorCodes.ValidationError, badShape.ErrorCode);
    Assert.Equal(0, f.Limiter.Calls);

    var unknownTarget = await f.Service.CreateAsync(Request(Guid.NewGuid()), TaskFixture.As(a));
    Assert.Equal(ErrorCodes.AgentNotFound, unknownTarget.ErrorCode);
    Assert.Equal(1, f.Limiter.Calls);

    var ghost = await f.Service.CreateAsync(Request(a.Id), TestData.AgentCaller(Guid.NewGuid(), "jobhunt"));
    Assert.Equal(ErrorCodes.AuthenticationFailed, ghost.ErrorCode);
    Assert.Equal(1, f.Limiter.Calls);
  }

  // ---------------------------------------------------------------- depth and cycle (A54, A55)

  [Fact]
  public async Task Cycle_a_b_c_a_is_rejected_without_a_task_and_details_hold_the_root_id_not_the_chain()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    var t1 = await f.CreateAsync(a, b);
    var t2 = await f.CreateAsync(b, c, t1);
    var before = f.Tasks.Tasks.Count;

    var result = await f.Service.CreateAsync(Request(a.Id, t2.Id), TaskFixture.As(c));

    Assert.Equal(ErrorCodes.CycleDetected, result.ErrorCode);
    Assert.Equal(ResultStatus.Conflict, result.ResultStatus);
    Assert.False(result.Retryable);
    Assert.Equal(t1.Id.ToString(), result.StructuredDetails!.Value.GetProperty("rootTaskId").GetString());
    Assert.DoesNotContain("ancestor", result.StructuredDetails.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
    Assert.Equal(before, f.Tasks.Tasks.Count);
    Assert.Equal(ErrorCodes.CycleDetected, RejectionCode(f));
  }

  [Fact]
  public async Task Self_call_and_a_call_back_to_a_middle_agent_are_cycles()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();

    Assert.Equal(ErrorCodes.CycleDetected, (await f.Service.CreateAsync(Request(a.Id), TaskFixture.As(a))).ErrorCode);

    var t1 = await f.CreateAsync(a, b);
    var t2 = await f.CreateAsync(b, c, t1);
    Assert.Equal(ErrorCodes.CycleDetected, (await f.Service.CreateAsync(Request(b.Id, t2.Id), TaskFixture.As(c))).ErrorCode);
  }

  [Fact]
  public async Task Sibling_branches_of_one_root_are_accepted()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    var d = f.AddAgent();
    var t1 = await f.CreateAsync(a, b);
    await f.CreateAsync(b, c, t1);

    var sibling = await f.Service.CreateAsync(Request(d.Id, t1.Id), TaskFixture.As(b));

    Assert.True(sibling.IsSuccess);
  }

  [Fact]
  public async Task Depth_above_the_maximum_is_rejected_with_max_depth_and_depth()
  {
    var f = new TaskFixture();
    f.Policy.MaxRecursionDepth = 2;
    var agents = Enumerable.Range(0, 5).Select(_ => f.AddAgent()).ToList();
    var t1 = await f.CreateAsync(agents[0], agents[1]);
    var t2 = await f.CreateAsync(agents[1], agents[2], t1);
    var t3 = await f.CreateAsync(agents[2], agents[3], t2); // depth 2 is still allowed
    Assert.Equal(2, t3.Depth);

    var result = await f.Service.CreateAsync(Request(agents[4].Id, t3.Id), TaskFixture.As(agents[3]));

    Assert.Equal(ErrorCodes.DepthExceeded, result.ErrorCode);
    Assert.False(result.Retryable);
    Assert.Equal(2, result.StructuredDetails!.Value.GetProperty("maxDepth").GetInt32());
    Assert.Equal(3, result.StructuredDetails.Value.GetProperty("depth").GetInt32());
    Assert.Equal(3, f.Tasks.Tasks.Count);
  }

  [Fact]
  public async Task Recursion_rejection_reaches_the_ack_with_the_a57_shape()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();

    var result = await f.Service.CreateAsync(Request(a.Id), TaskFixture.As(a));
    var payload = Application.Mappings.TaskMappings.ToErrorPayload(result);

    Assert.Equal(ErrorCodes.CycleDetected, payload.Code);
    Assert.False(payload.Retryable);
    Assert.True(payload.Details!.Value.TryGetProperty("rootTaskId", out _));
  }

  // ---------------------------------------------------------------- orphan flag (ADR-W8-02)

  [Fact]
  public async Task Orphan_flag_is_set_for_a_root_task_of_an_agent_that_holds_a_working_task()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    await f.CreateAsync(a, b); // b now holds a working task

    var orphan = await f.Service.CreateAsync(Request(c.Id), TaskFixture.As(b));
    var plain = await f.Service.CreateAsync(Request(c.Id), TaskFixture.As(a));

    Assert.True(orphan.IsSuccess);
    Assert.True(plain.IsSuccess);
    Assert.True(CreatedDetails(f, orphan.Data!.Id).GetProperty("orphanSuspect").GetBoolean());
    Assert.False(CreatedDetails(f, plain.Data!.Id).TryGetProperty("orphanSuspect", out _));
  }

  [Fact]
  public async Task Orphan_flag_is_absent_for_a_child_task()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var c = f.AddAgent();
    var parent = await f.CreateAsync(a, b);

    var child = await f.CreateAsync(b, c, parent);

    Assert.False(CreatedDetails(f, child.Id).TryGetProperty("orphanSuspect", out _));
  }

  private static string? RejectionCode(TaskFixture f)
  {
    var rejected = f.Audit.Events.Last(e => e.EventType == AuditEventTypes.PolicyRejected);
    return rejected.Details!.Value.GetProperty("errorCode").GetString();
  }

  private static JsonElement CreatedDetails(TaskFixture f, Guid taskId) =>
    f.Audit.Events.Single(e => e.EventType == AuditEventTypes.TaskCreated && e.TaskId == taskId).Details!.Value;
}

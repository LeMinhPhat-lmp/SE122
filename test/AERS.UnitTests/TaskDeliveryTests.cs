using System.Text.Json;
using Application.Constants;
using Application.DTOs.Tasks;
using Application.Models;
using Application.Options;
using Application.Services;
using Domain.Enums;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class TaskDeliveryTests
{
  private static readonly TaskResult Completed = new(Guid.NewGuid().ToString(), "completed", TaskFixture.Json("{\"ok\":true}"), null);

  // ---------------------------------------------------------------- TaskNotifier (A-03)

  private static (TaskNotifier Notifier, FakeConnectionRegistry Connections, FakeMessageRelay Relay, CapturingLogger<TaskNotifier> Log) NewNotifier()
  {
    var connections = new FakeConnectionRegistry();
    var relay = new FakeMessageRelay();
    var log = new CapturingLogger<TaskNotifier>();
    return (new TaskNotifier(connections, relay, log), connections, relay, log);
  }

  [Fact]
  public async Task Result_is_pushed_as_task_result_with_explicit_nulls()
  {
    var (notifier, connections, relay, _) = NewNotifier();
    var caller = Guid.NewGuid();
    connections.Connections[caller] = new ConnectionInfo("conn-a", "test-1");

    await notifier.NotifyResultAsync(caller, Completed);

    var (target, envelope) = Assert.Single(relay.Sent);
    Assert.Equal("conn-a", target.ConnectionId);
    Assert.Equal(TaskMessageTypes.Result, envelope.MessageType);
    Assert.Equal("completed", envelope.Payload.GetProperty("status").GetString());
    Assert.Equal(JsonValueKind.Null, envelope.Payload.GetProperty("error").ValueKind);
  }

  [Fact]
  public async Task Working_event_has_all_three_members_and_a_null_result()
  {
    var (notifier, connections, relay, _) = NewNotifier();
    var caller = Guid.NewGuid();
    var taskId = Guid.NewGuid();
    connections.Connections[caller] = new ConnectionInfo("conn-a", "test-1");

    await notifier.NotifyWorkingAsync(caller, taskId);

    var envelope = Assert.Single(relay.Sent).Envelope;
    Assert.Equal(TaskMessageTypes.StatusEvent, envelope.MessageType);
    Assert.Equal(taskId.ToString(), envelope.Payload.GetProperty("taskId").GetString());
    Assert.Equal("working", envelope.Payload.GetProperty("status").GetString());
    Assert.Equal(JsonValueKind.Null, envelope.Payload.GetProperty("result").ValueKind);
  }

  [Fact]
  public async Task Notifier_never_throws_without_a_connection_on_send_failure_or_registry_failure()
  {
    var (notifier, connections, relay, log) = NewNotifier();
    var caller = Guid.NewGuid();

    await notifier.NotifyResultAsync(caller, Completed);                 // no connection
    Assert.Empty(relay.Sent);

    connections.Connections[caller] = new ConnectionInfo("conn-a", "test-1");
    relay.FailWith = new InvalidOperationException("send failed");
    await notifier.NotifyResultAsync(caller, Completed);                 // send throws
    await notifier.NotifyWorkingAsync(caller, Guid.NewGuid());

    connections.FailOnResolve = true;
    await notifier.NotifyResultAsync(caller, Completed);                 // registry down

    Assert.Equal(3, log.Warnings.Count());
  }

  // ---------------------------------------------------------------- TaskRecoveryService (A-04)

  private static async Task<(TaskRecoveryService Service, TaskFixture F, FakeMessageRelay Relay, Domain.Entities.Agent Caller, Domain.Entities.Agent Target)> NewRecoveryAsync(int batch = 100)
  {
    var f = new TaskFixture();
    var caller = f.AddAgent();
    var target = f.AddAgent();
    f.Connections.Connections[caller.Id] = new ConnectionInfo("conn-a", "test-1");
    var relay = new FakeMessageRelay();
    var service = new TaskRecoveryService(
      f.UnitOfWork, f.Connections, relay,
      Microsoft.Extensions.Options.Options.Create(new TaskOptions { RedeliveryBatchSize = batch }),
      new CapturingLogger<TaskRecoveryService>());
    await Task.CompletedTask;
    return (service, f, relay, caller, target);
  }

  [Fact]
  public async Task Redelivery_pushes_unacknowledged_terminal_tasks_of_the_caller_only()
  {
    var (service, f, relay, caller, target) = await NewRecoveryAsync();
    var done = await f.CreateAsync(caller, target);
    done.Status = AgentTaskStatus.Completed;
    done.CompletedAt = f.Time.GetUtcNow();
    done.Output = TaskFixture.Json("{\"x\":1}");
    var failed = await f.CreateAsync(caller, target);
    failed.Status = AgentTaskStatus.Failed;
    failed.CompletedAt = f.Time.GetUtcNow().AddSeconds(1);
    var acknowledged = await f.CreateAsync(caller, target);
    acknowledged.Status = AgentTaskStatus.Completed;
    acknowledged.CompletedAt = f.Time.GetUtcNow();
    acknowledged.AcknowledgedAt = f.Time.GetUtcNow();
    await f.CreateAsync(caller, target);                                // still working: never replayed
    var otherCaller = await f.CreateAsync(target, caller);
    otherCaller.Status = AgentTaskStatus.Completed;                      // belongs to another caller

    await service.RedeliverAsync(caller.Id);

    Assert.Equal(2, relay.Sent.Count);
    Assert.All(relay.Sent, sent => Assert.Equal(TaskMessageTypes.Result, sent.Envelope.MessageType));
    Assert.Equal(done.Id.ToString(), relay.Sent[0].Envelope.Payload.GetProperty("taskId").GetString());
    Assert.Equal(failed.Id.ToString(), relay.Sent[1].Envelope.Payload.GetProperty("taskId").GetString());

    // Nothing was deleted: without an ack the same results come again on the next reconnect (at least once).
    await service.RedeliverAsync(caller.Id);
    Assert.Equal(4, relay.Sent.Count);
  }

  [Fact]
  public async Task Redelivery_respects_the_batch_limit_and_stops_after_acks()
  {
    var (service, f, relay, caller, target) = await NewRecoveryAsync(batch: 2);
    for (var i = 0; i < 5; i++)
    {
      var task = await f.CreateAsync(caller, target);
      task.Status = AgentTaskStatus.Completed;
      task.CompletedAt = f.Time.GetUtcNow().AddSeconds(i);
    }

    await service.RedeliverAsync(caller.Id);
    Assert.Equal(2, relay.Sent.Count);

    foreach (var task in f.Tasks.Tasks.Values)
      task.AcknowledgedAt = f.Time.GetUtcNow();
    relay.Sent.Clear();
    await service.RedeliverAsync(caller.Id);
    Assert.Empty(relay.Sent);
  }

  [Fact]
  public async Task Redelivery_without_a_connection_or_with_a_failing_send_never_throws()
  {
    var (service, f, relay, caller, target) = await NewRecoveryAsync();
    var task = await f.CreateAsync(caller, target);
    task.Status = AgentTaskStatus.Completed;

    f.Connections.Connections.Remove(caller.Id);
    await service.RedeliverAsync(caller.Id);
    Assert.Empty(relay.Sent);

    f.Connections.Connections[caller.Id] = new ConnectionInfo("conn-a", "test-1");
    relay.FailWith = new InvalidOperationException("send failed");
    await service.RedeliverAsync(caller.Id);

    Assert.Equal(AgentTaskStatus.Completed, task.Status);
    Assert.Null(task.AcknowledgedAt);
  }

  [Fact]
  public async Task Target_reconnecting_on_a_new_connection_can_still_report_its_working_task()
  {
    var f = new TaskFixture();
    var a = f.AddAgent();
    var b = f.AddAgent();
    var task = await f.CreateAsync(a, b);
    await f.Connections.BindAsync(b.Id, "old-connection", "test-1");
    await f.Connections.UnbindAsync(b.Id, "old-connection");
    await f.Connections.BindAsync(b.Id, "new-connection", "test-1");

    var received = await f.Service.ReceiveResultAsync(new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));

    Assert.True(received.IsSuccess);
    Assert.Equal(AgentTaskStatus.Completed, task.Status);
  }
}

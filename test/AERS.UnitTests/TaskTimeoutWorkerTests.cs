using API.Workers;
using Application.Constants;
using Application.DTOs.Tasks;
using Application.Interfaces.Features;
using Application.Interfaces.Realtime;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Models;
using Application.Options;
using Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class TaskTimeoutWorkerTests
{
  private sealed class Harness
  {
    public TaskFixture F { get; } = new();
    public FakeNotifier Notifier { get; } = new();
    public FakeDisconnectTracker Tracker { get; } = new();
    public TaskTimeoutWorker Worker { get; }

    public Harness(int batchSize = 100, int graceSeconds = 30)
    {
      var services = new ServiceCollection();
      services.AddSingleton<ITaskService>(F.Service);
      services.AddSingleton<IUnitOfWork>(F.UnitOfWork);
      services.AddSingleton<ITaskNotifier>(Notifier);
      services.AddSingleton<IDisconnectTracker>(Tracker);
      services.AddSingleton<IConnectionRegistry>(F.Connections);

      Worker = new TaskTimeoutWorker(
        services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
        Microsoft.Extensions.Options.Options.Create(new TaskOptions { TimeoutBatchSize = batchSize, TargetDisconnectGraceSeconds = graceSeconds }),
        F.Time,
        NullLogger<TaskTimeoutWorker>.Instance);
    }
  }

  [Fact]
  public async Task Tick_expires_only_due_tasks_and_pushes_for_each()
  {
    var h = new Harness();
    var a = h.F.AddAgent();
    var b = h.F.AddAgent();
    var due = await h.F.CreateAsync(a, b);
    h.F.Time.Advance(TimeSpan.FromSeconds(200));
    var later = await h.F.CreateAsync(a, b);                 // deadline 200 seconds after the first
    h.F.Time.Advance(TimeSpan.FromSeconds(101));             // first is overdue, second is not

    var failed = await h.Worker.RunOnceAsync(CancellationToken.None);

    Assert.Equal(1, failed);
    Assert.Equal(AgentTaskStatus.Failed, due.Status);
    Assert.Equal(AgentTaskStatus.Working, later.Status);
    var (callerId, result) = Assert.Single(h.Notifier.Results);
    Assert.Equal(a.Id, callerId);
    Assert.Equal(ErrorCodes.TaskTimeout, result.Error!.Code);
  }

  [Fact]
  public async Task Tick_continues_over_full_batches()
  {
    var h = new Harness(batchSize: 2);
    var a = h.F.AddAgent();
    var b = h.F.AddAgent();
    for (var i = 0; i < 5; i++)
      await h.F.CreateAsync(a, b);
    h.F.Time.Advance(TimeSpan.FromSeconds(301));

    var failed = await h.Worker.RunOnceAsync(CancellationToken.None);

    Assert.Equal(5, failed);
    Assert.Equal(5, h.Notifier.Results.Count);
  }

  [Fact]
  public async Task A_lost_compare_and_set_pushes_nothing()
  {
    var h = new Harness();
    await h.F.CreateAsync(h.F.AddAgent(), h.F.AddAgent());
    h.F.Time.Advance(TimeSpan.FromSeconds(301));
    h.F.Tasks.FailNextTransition = true;

    var failed = await h.Worker.RunOnceAsync(CancellationToken.None);

    Assert.Equal(0, failed);
    Assert.Empty(h.Notifier.Results);
  }

  [Fact]
  public async Task Target_that_stays_away_past_the_grace_period_fails_its_working_tasks()
  {
    var h = new Harness(graceSeconds: 30);
    var a = h.F.AddAgent();
    var b = h.F.AddAgent();
    var task = await h.F.CreateAsync(a, b);
    h.Tracker.Marks[b.Id] = h.F.Time.GetUtcNow();

    h.F.Time.Advance(TimeSpan.FromSeconds(10));
    Assert.Equal(0, await h.Worker.RunOnceAsync(CancellationToken.None));       // still inside the grace period
    Assert.Equal(AgentTaskStatus.Working, task.Status);

    h.F.Time.Advance(TimeSpan.FromSeconds(25));
    Assert.Equal(1, await h.Worker.RunOnceAsync(CancellationToken.None));
    Assert.Equal(AgentTaskStatus.Failed, task.Status);
    Assert.Equal(ErrorCodes.TargetUnavailable, task.Error!.Code);
    Assert.Equal(ErrorCodes.TargetUnavailable, Assert.Single(h.Notifier.Results).Result.Error!.Code);
    Assert.Empty(h.Tracker.Marks);                                                // nothing left to watch

    // A result that arrives afterwards loses: the task is already failed.
    var late = await h.F.Service.ReceiveResultAsync(
      new TaskResult(task.Id.ToString(), "completed", TaskFixture.Json("{}"), null), TaskFixture.As(b));
    Assert.Equal(ErrorCodes.TaskStateConflict, late.ErrorCode);
  }

  [Fact]
  public async Task Target_that_reconnected_is_not_failed_and_its_mark_is_cleared()
  {
    var h = new Harness(graceSeconds: 30);
    var a = h.F.AddAgent();
    var b = h.F.AddAgent();
    var task = await h.F.CreateAsync(a, b);
    h.Tracker.Marks[b.Id] = h.F.Time.GetUtcNow();
    h.F.Connections.Connections[b.Id] = new ConnectionInfo("conn-b", "test-1");
    h.F.Time.Advance(TimeSpan.FromSeconds(60));

    var failed = await h.Worker.RunOnceAsync(CancellationToken.None);

    Assert.Equal(0, failed);
    Assert.Equal(AgentTaskStatus.Working, task.Status);
    Assert.Empty(h.Tracker.Marks);
  }

  [Fact]
  public async Task Tracker_outage_does_not_stop_expiry_and_does_not_throw()
  {
    var h = new Harness();
    var task = await h.F.CreateAsync(h.F.AddAgent(), h.F.AddAgent());
    h.Tracker.FailWith = new InvalidOperationException("redis down");
    h.F.Time.Advance(TimeSpan.FromSeconds(301));

    var failed = await h.Worker.RunOnceAsync(CancellationToken.None);

    Assert.Equal(1, failed);
    Assert.Equal(AgentTaskStatus.Failed, task.Status);
  }

  [Fact]
  public async Task Two_workers_over_the_same_data_fail_each_task_once()
  {
    var h = new Harness();
    var second = new TaskTimeoutWorker(
      new ServiceCollection()
        .AddSingleton<ITaskService>(h.F.Service)
        .AddSingleton<IUnitOfWork>(h.F.UnitOfWork)
        .AddSingleton<ITaskNotifier>(h.Notifier)
        .AddSingleton<IDisconnectTracker>(h.Tracker)
        .AddSingleton<IConnectionRegistry>(h.F.Connections)
        .BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
      Microsoft.Extensions.Options.Options.Create(new TaskOptions()),
      h.F.Time,
      NullLogger<TaskTimeoutWorker>.Instance);
    await h.F.CreateAsync(h.F.AddAgent(), h.F.AddAgent());
    h.F.Time.Advance(TimeSpan.FromSeconds(301));

    var counts = await Task.WhenAll(h.Worker.RunOnceAsync(CancellationToken.None), second.RunOnceAsync(CancellationToken.None));

    Assert.Equal(1, counts.Sum());
    Assert.Single(h.F.Tasks.History);
  }
}

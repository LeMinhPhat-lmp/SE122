using Application.Interfaces.Features;
using Application.Interfaces.Realtime;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Options;
using Microsoft.Extensions.Options;

namespace API.Workers;

/// <summary>
/// Expires tasks past their deadline (A46) and fails the working tasks of targets that did not reconnect in time (ADR-W7-03).
/// Every instance runs it: each transition is a compare-and-set in PostgreSQL, so a task changes state exactly once. The push
/// to the caller happens after the commit and is best effort. A failed tick is logged and the loop continues.
/// </summary>
public sealed class TaskTimeoutWorker(
  IServiceScopeFactory scopeFactory,
  IOptions<TaskOptions> taskOptions,
  TimeProvider timeProvider,
  ILogger<TaskTimeoutWorker> logger) : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(taskOptions.Value.TimeoutScanIntervalSeconds), timeProvider);

    try
    {
      do
      {
        try
        {
          var failed = await RunOnceAsync(stoppingToken);
          logger.LogDebug("Timeout worker tick finished; {FailedCount} tasks failed.", failed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          logger.LogError(ex, "A timeout worker tick failed; it is retried on the next tick.");
        }
      }
      while (await timer.WaitForNextTickAsync(stoppingToken));
    }
    catch (OperationCanceledException)
    {
      // Stopping.
    }
  }

  /// <summary>One tick: expire every due task (repeating while a full batch comes back), then handle disconnected targets. Returns the number of tasks failed.</summary>
  public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
  {
    var failed = 0;
    int batchFailed;
    do
    {
      (var examined, batchFailed) = await ExpireBatchAsync(cancellationToken);
      failed += batchFailed;

      // A full batch without a single success would be returned again and again; leave it to the next tick.
      if (examined < taskOptions.Value.TimeoutBatchSize || batchFailed == 0)
        break;
    }
    while (true);

    // The grace step needs Redis; the expiry above needs only PostgreSQL and has already happened.
    try
    {
      failed += await FailDisconnectedTargetsAsync(cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "The disconnected target step could not run; tasks still end by TASK_TIMEOUT.");
    }

    return failed;
  }

  private async Task<(int Examined, int Expired)> ExpireBatchAsync(CancellationToken cancellationToken)
  {
    await using var scope = scopeFactory.CreateAsyncScope();
    var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
    var notifier = scope.ServiceProvider.GetRequiredService<ITaskNotifier>();

    var due = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TaskRepository
      .ListExpiredAsync(timeProvider.GetUtcNow(), taskOptions.Value.TimeoutBatchSize, cancellationToken);

    var expired = 0;
    foreach (var item in due)
    {
      try
      {
        var outcome = await taskService.ExpireAsync(item.Id, cancellationToken);
        if (!outcome.IsSuccess || !outcome.Data!.Expired)
          continue;

        expired++;
        logger.LogInformation("Task {TaskId} expired.", item.Id);
        await notifier.NotifyResultAsync(outcome.Data.CallerAgentId, outcome.Data.ResultToPush!, cancellationToken);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogWarning(ex, "Could not expire task {TaskId}.", item.Id);
      }
    }

    return (due.Count, expired);
  }

  private async Task<int> FailDisconnectedTargetsAsync(CancellationToken cancellationToken)
  {
    var settings = taskOptions.Value;
    await using var scope = scopeFactory.CreateAsyncScope();
    var tracker = scope.ServiceProvider.GetRequiredService<IDisconnectTracker>();
    var registry = scope.ServiceProvider.GetRequiredService<IConnectionRegistry>();
    var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();
    var notifier = scope.ServiceProvider.GetRequiredService<ITaskNotifier>();

    var cutoff = timeProvider.GetUtcNow().AddSeconds(-settings.TargetDisconnectGraceSeconds);
    var due = await tracker.ListDueAsync(cutoff, settings.TimeoutBatchSize, cancellationToken);

    var failed = 0;
    foreach (var agentId in due)
    {
      try
      {
        // A target that is connected again is fine; its mark is stale.
        if (await registry.ResolveAsync(agentId, cancellationToken) is not null)
        {
          await tracker.ClearAsync(agentId, cancellationToken);
          continue;
        }

        var batch = await taskService.FailTasksOfDisconnectedTargetAsync(agentId, settings.TimeoutBatchSize, cancellationToken);
        if (!batch.IsSuccess)
          continue;

        foreach (var outcome in batch.Data!.Failed)
        {
          logger.LogInformation("Task {TaskId} failed because target agent {AgentId} did not reconnect.", outcome.TaskId, agentId);
          await notifier.NotifyResultAsync(outcome.CallerAgentId, outcome.ResultToPush!, cancellationToken);
        }

        failed += batch.Data.Failed.Count;
        if (batch.Data.Examined < settings.TimeoutBatchSize)
          await tracker.ClearAsync(agentId, cancellationToken);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogWarning(ex, "Could not process the disconnected target agent {AgentId}.", agentId);
      }
    }

    return failed;
  }
}

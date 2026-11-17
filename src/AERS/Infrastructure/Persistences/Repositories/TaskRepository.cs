using System.Text.Json;
using Application.Interfaces.Repositories;
using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistences.Repositories;

public sealed class TaskRepository(AersDbContext context) : ITaskRepository
{
  public Task<AgentTask?> GetByIdAsync(Guid taskId, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentTasks.AsQueryable();
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(task => task.Id == taskId, cancellationToken);
  }

  public Task AddAsync(AgentTask task, CancellationToken cancellationToken = default)
  {
    return context.AgentTasks.AddAsync(task, cancellationToken).AsTask();
  }

  public Task AddHistoryAsync(TaskStatusHistory entry, CancellationToken cancellationToken = default)
  {
    return context.TaskStatusHistories.AddAsync(entry, cancellationToken).AsTask();
  }

  // Why ExecuteUpdateAsync and not an optimistic concurrency token: the check and the write are one SQL statement
  // (UPDATE ... WHERE id = @id AND status = @expected), so two concurrent callers cannot both match the expected status,
  // and no entity has to be loaded and tracked first. The statement runs on the DbContext connection, so it takes part in
  // the transaction opened by IUnitOfWork.StartTransaction. tasks has no updated_at column, so nothing is bypassed.
  public async Task<bool> TryTransitionAsync(
    Guid taskId,
    AgentTaskStatus expected,
    AgentTaskStatus next,
    DateTimeOffset now,
    JsonElement? output,
    TaskError? error,
    CancellationToken cancellationToken = default)
  {
    var changedRows = await context.AgentTasks
      .Where(task => task.Id == taskId && task.Status == expected)
      .ExecuteUpdateAsync(setters =>
      {
        setters.SetProperty(task => task.Status, next);

        if (next == AgentTaskStatus.Working)
          setters.SetProperty(task => task.StartedAt, (DateTimeOffset?)now);

        if (next.IsTerminal())
        {
          setters.SetProperty(task => task.CompletedAt, (DateTimeOffset?)now);
          setters.SetProperty(task => task.Output, output);
          setters.SetProperty(task => task.Error, error);
        }
      }, cancellationToken);

    return changedRows == 1;
  }

  // Served by the (status, deadline_at) index; projects two columns, so no entity is materialized or tracked.
  public async Task<IReadOnlyList<ExpiredTaskRef>> ListExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
    await context.AgentTasks
      .AsNoTracking()
      .Where(task => (task.Status == AgentTaskStatus.Submitted || task.Status == AgentTaskStatus.Working) && task.DeadlineAt < now)
      .OrderBy(task => task.DeadlineAt)
      .ThenBy(task => task.Id)
      .Select(task => new ExpiredTaskRef(task.Id, task.Status))
      .Take(limit)
      .ToListAsync(cancellationToken);

  public async Task<IReadOnlyList<WorkingTaskRef>> ListWorkingByTargetAsync(Guid targetAgentId, int limit, CancellationToken cancellationToken = default) =>
    await context.AgentTasks
      .AsNoTracking()
      .Where(task => task.TargetAgentId == targetAgentId && task.Status == AgentTaskStatus.Working)
      .OrderBy(task => task.CreatedAt)
      .ThenBy(task => task.Id)
      .Select(task => new WorkingTaskRef(task.Id, task.CallerAgentId, task.DeadlineAt))
      .Take(limit)
      .ToListAsync(cancellationToken);

  public async Task<IReadOnlyList<AgentTask>> ListUnacknowledgedTerminalAsync(Guid callerAgentId, int limit, CancellationToken cancellationToken = default) =>
    await context.AgentTasks
      .AsNoTracking()
      .Where(task => task.CallerAgentId == callerAgentId
        && (task.Status == AgentTaskStatus.Completed || task.Status == AgentTaskStatus.Failed)
        && task.AcknowledgedAt == null)
      .OrderBy(task => task.CompletedAt)
      .ThenBy(task => task.Id)
      .Take(limit)
      .ToListAsync(cancellationToken);

  // One statement, no transaction needed. The AcknowledgedAt == null predicate makes a repeated ack a no-op.
  public async Task<bool> TryAcknowledgeAsync(Guid taskId, Guid callerAgentId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
    await context.AgentTasks
      .Where(task => task.Id == taskId && task.CallerAgentId == callerAgentId && task.AcknowledgedAt == null)
      .ExecuteUpdateAsync(setters => setters.SetProperty(task => task.AcknowledgedAt, (DateTimeOffset?)now), cancellationToken) == 1;

  public Task<int> CountWorkingAsTargetAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    context.AgentTasks
      .AsNoTracking()
      .Where(task => task.TargetAgentId == agentId && task.Status == AgentTaskStatus.Working)
      .Take(1)
      .CountAsync(cancellationToken);
}

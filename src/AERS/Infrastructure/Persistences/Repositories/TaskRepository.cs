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
}

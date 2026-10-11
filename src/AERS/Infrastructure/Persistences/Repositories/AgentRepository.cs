using Application.Interfaces.Repositories;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistences.Repositories;

public sealed class AgentRepository(AersDbContext context) : IAgentRepository
{
  public Task AddAsync(Agent entity, CancellationToken cancellationToken = default)
  {
    return context.AddAsync(entity, cancellationToken).AsTask();
  }

  public Task AddRangeAsync(IEnumerable<Agent> entities, CancellationToken cancellationToken = default)
  {
    return context.AddRangeAsync(entities, cancellationToken);
  }

  public void Delete(Agent entity)
  {
    entity.DeletedAt = DateTimeOffset.UtcNow;
  }

  public void DeleteRange(IEnumerable<Agent> entities)
  {
    var now = DateTimeOffset.UtcNow;

    foreach (var entity in entities)
      entity.DeletedAt = now;
  }

  public Task<Agent?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.Agents.AsQueryable().Where(agent => !agent.IsDeleted);
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(agent => agent.Id == entityId, cancellationToken);
  }

  public Task<List<Agent>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.Agents.AsQueryable().Where(agent => !agent.IsDeleted);
    if (!trackChanges)
      query = query.AsNoTracking();
    if (skip.HasValue)
      query = query.Skip((int)skip);
    if (take.HasValue)
      query = query.Take((int)take);
    return query.ToListAsync(cancellationToken);
  }

  public void Remove(Agent entity)
  {
    context.Remove(entity);
  }

  public void RemoveRange(IEnumerable<Agent> entities)
  {
    context.RemoveRange(entities);
  }

  public void Update(Agent entity)
  {
    context.Update(entity);
  }
}
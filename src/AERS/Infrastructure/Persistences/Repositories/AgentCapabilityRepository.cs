using Application.Interfaces.Repositories;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistences.Repositories;

public sealed class AgentCapabilityRepository(AersDbContext context) : IAgentCapabilityRepository
{
  public Task AddAsync(AgentCapability entity, CancellationToken cancellationToken = default)
  {
    return context.AgentCapabilities.AddAsync(entity, cancellationToken).AsTask();
  }

  public Task AddRangeAsync(IEnumerable<AgentCapability> entities, CancellationToken cancellationToken = default)
  {
    return context.AgentCapabilities.AddRangeAsync(entities, cancellationToken);
  }

  public void Delete(AgentCapability entity)
  {
    entity.DeletedAt = DateTimeOffset.UtcNow;
  }

  public void DeleteRange(IEnumerable<AgentCapability> entities)
  {
    var now = DateTimeOffset.UtcNow;

    foreach (var entity in entities)
      entity.DeletedAt = now;
  }

  public Task<AgentCapability?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentCapabilities.AsQueryable().Where(capability => !capability.IsDeleted);
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(capability => capability.Id == entityId, cancellationToken);
  }

  public Task<List<AgentCapability>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentCapabilities.AsQueryable().Where(capability => !capability.IsDeleted);
    if (!trackChanges)
      query = query.AsNoTracking();
    if (skip.HasValue)
      query = query.Skip((int)skip);
    if (take.HasValue)
      query = query.Take((int)take);
    return query.ToListAsync(cancellationToken);
  }

  public void Remove(AgentCapability entity)
  {
    context.Remove(entity);
  }

  public void RemoveRange(IEnumerable<AgentCapability> entities)
  {
    context.RemoveRange(entities);
  }

  public void Update(AgentCapability entity)
  {
    context.Update(entity);
  }
}
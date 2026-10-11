using Application.Interfaces.Repositories;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistences.Repositories;

public sealed class AgentCredentialRepository(AersDbContext context) : IAgentCredentialRepository
{
  public Task AddAsync(AgentCredential entity, CancellationToken cancellationToken = default)
  {
    return context.AddAsync(entity, cancellationToken).AsTask();
  }

  public Task AddRangeAsync(IEnumerable<AgentCredential> entities, CancellationToken cancellationToken = default)
  {
    return context.AddRangeAsync(entities, cancellationToken);
  }

  public Task<AgentCredential?> GetByAgentIdAsync(Guid targetAgentId, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentCredentials.AsQueryable();
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(credential => credential.AgentId == targetAgentId, cancellationToken);
  }

  public Task<AgentCredential?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentCredentials.AsQueryable();
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(credential => credential.Id == entityId, cancellationToken);
  }

  public Task<List<AgentCredential>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentCredentials.AsQueryable();
    if (!trackChanges)
      query = query.AsNoTracking();
    if (skip.HasValue)
      query = query.Skip((int)skip);
    if (take.HasValue)
      query = query.Take((int)take);
    return query.ToListAsync(cancellationToken);
  }

  public void Remove(AgentCredential entity)
  {
    context.Remove(entity);
  }

  public void RemoveRange(IEnumerable<AgentCredential> entities)
  {
    context.RemoveRange(entities);
  }

  public void Update(AgentCredential entity)
  {
    context.Update(entity);
  }
}
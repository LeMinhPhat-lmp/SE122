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

  public Task<List<AgentCredential>> ListByAgentIdAsync(Guid agentId, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.AgentCredentials.AsQueryable().Where(credential => credential.AgentId == agentId);
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.ToListAsync(cancellationToken);
  }

  public Task<AgentCredential?> GetActiveByHashAsync(string apiKeyHash, CancellationToken cancellationToken = default)
  {
    return context.AgentCredentials
      .AsNoTracking()
      .Include(credential => credential.Agent)
      .FirstOrDefaultAsync(
        credential => credential.ApiKeyHash == apiKeyHash
          && credential.RevokedAt == null
          && credential.Agent.DeletedAt == null,
        cancellationToken);
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
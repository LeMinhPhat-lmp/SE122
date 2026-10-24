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
    var query = context.Agents.AsQueryable().Where(agent => agent.DeletedAt == null);
    if (!trackChanges)
      query = query.AsNoTracking();
    return query.FirstOrDefaultAsync(agent => agent.Id == entityId, cancellationToken);
  }

  public Task<Agent?> GetWithCapabilitiesAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    return context.Agents
      .AsNoTracking()
      .Include(agent => agent.Capabilities.Where(capability => capability.DeletedAt == null))
      .FirstOrDefaultAsync(agent => agent.Id == agentId && agent.DeletedAt == null, cancellationToken);
  }

  public async Task<AgentPage> ListActiveAsync(string domain, string? capabilityName, DiscoveryCursorPosition? after, int limit,
    CancellationToken cancellationToken = default)
  {
    // Row-value comparison keeps both the keyset predicate and the ordering inside PostgreSQL, where uuid ordering is
    // defined (the .NET Guid order differs). Plain LINQ would be needed only for the first page.
    var source = after is null
      ? context.Agents.AsQueryable()
      : context.Agents.FromSql($"""
          SELECT * FROM agents
          WHERE (registered_at, id) > ({after.RegisteredAt.ToUniversalTime()}, {after.Id})
          """);

    var query = source
      .AsNoTracking()
      .Where(agent => agent.DeletedAt == null && agent.Domain == domain);

    if (capabilityName is not null)
      query = query.Where(agent => agent.Capabilities.Any(capability => capability.Name == capabilityName && capability.DeletedAt == null));

    // One extra row tells whether a next page exists; it is never returned.
    var rows = await query
      .Include(agent => agent.Capabilities.Where(capability => capability.DeletedAt == null))
      .AsSplitQuery()
      .OrderBy(agent => agent.CreatedAt)
      .ThenBy(agent => agent.Id)
      .Take(limit + 1)
      .ToListAsync(cancellationToken);

    return new AgentPage(rows.Take(limit).ToList(), rows.Count > limit);
  }

  public Task<List<Agent>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default)
  {
    var query = context.Agents.AsQueryable().Where(agent => agent.DeletedAt == null);
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
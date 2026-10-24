using System.Text.Json;
using Application.DTOs.Agent.Requests;
using Application.Interfaces;
using Application.Interfaces.Policies;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Interfaces.Security;
using Application.Models;
using Application.Options;
using Application.Services;
using Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AERS.UnitTests;

internal sealed class FakeAgentRepository : IAgentRepository
{
  public List<Agent> Agents { get; } = [];
  public AgentPage PageToReturn { get; set; } = new([], false);
  public int ListCalls { get; private set; }
  public (string Domain, string? Capability, DiscoveryCursorPosition? After, int Limit)? LastListArguments { get; private set; }

  public Task<Agent?> GetWithCapabilitiesAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    Task.FromResult(Agents.FirstOrDefault(agent => agent.Id == agentId && !agent.IsDeleted));

  public Task<AgentPage> ListActiveAsync(string domain, string? capabilityName, DiscoveryCursorPosition? after, int limit,
    CancellationToken cancellationToken = default)
  {
    ListCalls++;
    LastListArguments = (domain, capabilityName, after, limit);
    return Task.FromResult(PageToReturn);
  }

  public Task<Agent?> GetByIdAsync(Guid entityId, bool trackChanges = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<List<Agent>> ListEntitiesAsync(int? skip, int? take, bool trackChanges = false, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task AddAsync(Agent entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task AddRangeAsync(IEnumerable<Agent> entities, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public void Update(Agent entity) => throw new NotSupportedException();
  public void Remove(Agent entity) => throw new NotSupportedException();
  public void RemoveRange(IEnumerable<Agent> entities) => throw new NotSupportedException();
  public void Delete(Agent entity) => throw new NotSupportedException();
  public void DeleteRange(IEnumerable<Agent> entities) => throw new NotSupportedException();
}

internal sealed class FakeUnitOfWork(IAgentRepository agentRepository) : IUnitOfWork
{
  public IAgentRepository AgentRepository { get; } = agentRepository;
  public IAgentCapabilityRepository AgentCapabilityRepository => throw new NotSupportedException();
  public IAgentCredentialRepository AgentCredentialRepository => throw new NotSupportedException();
  public IAuditEventRepository AuditEventRepository => throw new NotSupportedException();
  public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<ITransaction> StartTransaction(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakePresenceStore : IAgentPresenceStore
{
  public HashSet<Guid> Online { get; } = [];
  public int BatchCalls { get; private set; }
  public Exception? FailWith { get; set; }

  public Task<IReadOnlyDictionary<Guid, bool>> GetOnlineStatusAsync(IReadOnlyCollection<Guid> agentIds, CancellationToken cancellationToken = default)
  {
    BatchCalls++;
    if (FailWith is not null)
      throw FailWith;
    return Task.FromResult<IReadOnlyDictionary<Guid, bool>>(agentIds.ToDictionary(id => id, id => Online.Contains(id)));
  }

  public Task MarkAliveAsync(Guid agentId, DateTimeOffset now, TimeSpan aliveTtl, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task<AgentPresence> GetAsync(Guid agentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
  public Task RemoveAsync(Guid agentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

internal sealed class FakeDomainAccessPolicy(Func<string, string, bool> rule) : IDomainAccessPolicy
{
  public int Calls { get; private set; }

  public bool CanAccess(string callerDomain, string targetDomain)
  {
    Calls++;
    return rule(callerDomain, targetDomain);
  }
}

internal sealed class UnusedApiKeyService : IApiKeyService
{
  public IssuedApiKey Generate() => throw new NotSupportedException();
  public string Hash(string rawKey) => throw new NotSupportedException();
}

internal static class TestData
{
  public static readonly DateTimeOffset BaseTime = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

  public static Agent CreateAgent(string domain, int minutesAfterBase = 0, params string[] capabilityNames)
  {
    var agent = Agent.Register(Guid.NewGuid(), $"agent-{domain}", domain, "test agent", null);
    agent.CreatedAt = BaseTime.AddMinutes(minutesAfterBase);

    var schema = JsonDocument.Parse("{}").RootElement.Clone();
    foreach (var name in capabilityNames)
      agent.AddCapability(name, $"{name} description", schema, schema);

    return agent;
  }

  public static AuthenticatedAgentContext AgentCaller(Guid agentId, string domain) => new(agentId, domain, "ApiKey");

  public static AuthenticatedAgentContext RegistrationKeyCaller() => new(null, null, "RegistrationKey");

  public static AgentRegistryService CreateService(
    FakeAgentRepository repository,
    FakePresenceStore presence,
    IDomainAccessPolicy policy,
    DiscoveryOptions? discovery = null) =>
    new(
      new FakeUnitOfWork(repository),
      new InlineValidator<AgentRegistrationRequest>(),
      new UnusedApiKeyService(),
      presence,
      Options.Create(new AersPolicyOptions()),
      Options.Create(discovery ?? new DiscoveryOptions()),
      policy,
      TimeProvider.System,
      NullLogger<AgentRegistryService>.Instance);
}

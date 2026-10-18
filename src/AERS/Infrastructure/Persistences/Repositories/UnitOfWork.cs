using Application.Interfaces;
using Application.Interfaces.Repositories;

namespace Infrastructure.Persistences.Repositories;

public sealed class UnitOfWork(
  AersDbContext context,
  IAgentCapabilityRepository agentCapabilityRepository,
  IAgentCredentialRepository agentCredentialRepository,
  IAgentRepository agentRepository,
  IAuditEventRepository auditEventRepository) : IUnitOfWork
{
  public IAgentCapabilityRepository AgentCapabilityRepository => agentCapabilityRepository;
  public IAgentCredentialRepository AgentCredentialRepository => agentCredentialRepository;
  public IAgentRepository AgentRepository => agentRepository;
  public IAuditEventRepository AuditEventRepository => auditEventRepository;

  public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
  {
    return context.SaveChangesAsync(cancellationToken);
  }

  public async Task<ITransaction> StartTransaction(CancellationToken cancellationToken = default)
  {
    var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    return new Transaction(transaction);
  }
}

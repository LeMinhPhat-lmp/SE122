namespace Application.Interfaces.Repositories;

public interface IUnitOfWork
{
  IAgentCapabilityRepository AgentCapabilityRepository { get; }
  IAgentCredentialRepository AgentCredentialRepository { get; }
  IAgentRepository AgentRepository { get; }
  IAuditEventRepository AuditEventRepository { get; }

  Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
  Task<ITransaction> StartTransaction(CancellationToken cancellationToken = default);
}

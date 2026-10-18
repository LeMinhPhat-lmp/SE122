using Application.Interfaces.Repositories;
using Domain.Entities;

namespace Infrastructure.Persistences.Repositories;

public sealed class AuditEventRepository(AersDbContext context) : IAuditEventRepository
{
  public Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default)
  {
    return context.AuditEvents.AddAsync(auditEvent, cancellationToken).AsTask();
  }
}

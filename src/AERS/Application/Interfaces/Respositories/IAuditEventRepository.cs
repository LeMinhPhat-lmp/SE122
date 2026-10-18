using Domain.Entities;

namespace Application.Interfaces.Repositories;

public interface IAuditEventRepository
{
  Task AddAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

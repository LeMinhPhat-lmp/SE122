using Application.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistences.Repositories;

public sealed class Transaction(IDbContextTransaction transaction) : ITransaction
{
  public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
  {
    return transaction.CommitAsync(cancellationToken);
  }

  public ValueTask DisposeAsync()
  {
    return transaction.DisposeAsync();
  }

  public Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
  {
    return transaction.RollbackAsync(cancellationToken);
  }
}
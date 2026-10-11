namespace Application.Interfaces;

public interface ITransaction : IAsyncDisposable
{
  Task CommitTransactionAsync(CancellationToken cancellationToken = default);
  Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
}
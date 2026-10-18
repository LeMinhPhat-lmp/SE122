using Application.Exceptions;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// Base class for Redis-backed stores. It is the single place where Redis failures are translated into
/// <see cref="StateStoreUnavailableException"/>, so the Application layer never depends on Redis types
/// and every future Redis store (rate limiter, task state, connection registry) behaves the same way.
/// </summary>
public abstract class RedisStoreBase(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
{
  protected string KeyPrefix => options.Value.KeyPrefix;

  protected async Task<T> ExecuteAsync<T>(Func<IDatabase, Task<T>> operation, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();

    try
    {
      return await operation(redis.GetDatabase());
    }
    catch (RedisException ex)
    {
      throw new StateStoreUnavailableException(ex);
    }
  }

  protected Task ExecuteAsync(Func<IDatabase, Task> operation, CancellationToken cancellationToken) =>
    ExecuteAsync<bool>(async database =>
    {
      await operation(database);
      return true;
    }, cancellationToken);
}

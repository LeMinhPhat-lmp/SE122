using Application.Interfaces.Runtime;
using Infrastructure.Persistences;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

public sealed class DependencyHealthChecker(AersDbContext context, IConnectionMultiplexer redis) : IDependencyHealthChecker
{
  public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
  {
    // Catching is the purpose of this class: any connectivity failure must become "unhealthy" (HTTP 503), not a 500.
    try
    {
      if (!await context.Database.CanConnectAsync(cancellationToken))
        return false;

      await redis.GetDatabase().PingAsync();
      return true;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      return false;
    }
  }
}

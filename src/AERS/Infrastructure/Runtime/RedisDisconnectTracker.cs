using Application.Interfaces.Runtime;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>Sorted set {prefix}disconnected: member agentId, score unix milliseconds of the disconnect (ADR-W7-03).</summary>
public sealed class RedisDisconnectTracker(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
  : RedisStoreBase(redis, options), IDisconnectTracker
{
  private string Key => $"{KeyPrefix}disconnected";

  public Task MarkDisconnectedAsync(Guid agentId, DateTimeOffset at, CancellationToken cancellationToken = default) =>
    ExecuteAsync(database => database.SortedSetAddAsync(Key, agentId.ToString(), at.ToUnixTimeMilliseconds()), cancellationToken);

  public Task ClearAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    ExecuteAsync(database => database.SortedSetRemoveAsync(Key, agentId.ToString()), cancellationToken);

  public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset notAfter, int limit, CancellationToken cancellationToken = default) =>
    ExecuteAsync<IReadOnlyList<Guid>>(async database =>
    {
      var members = await database.SortedSetRangeByScoreAsync(
        Key, double.NegativeInfinity, notAfter.ToUnixTimeMilliseconds(), Exclude.None, Order.Ascending, 0, limit);

      return members.Select(member => Guid.TryParse(member.ToString(), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
    }, cancellationToken);
}

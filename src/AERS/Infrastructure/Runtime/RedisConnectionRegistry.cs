using System.Globalization;
using Application.Interfaces.Runtime;
using Application.Models;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// Agent to SignalR connection mapping (decisions A30 and A31): hash {prefix}conn:{agentId} with connectionId,
/// instanceId and connectedAt (ISO-8601 UTC). One connection per agent, last connection wins, no TTL.
/// </summary>
public sealed class RedisConnectionRegistry(IConnectionMultiplexer redis, IOptions<RedisOptions> options, TimeProvider timeProvider)
  : RedisStoreBase(redis, options), IConnectionRegistry
{
  // Atomic compare-and-delete: an old connection closing must never remove a newer mapping.
  private const string UnbindScript =
    "if redis.call('HGET', KEYS[1], 'connectionId') == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

  private const string PurgeScript =
    "if redis.call('HGET', KEYS[1], 'instanceId') == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";

  private string Key(Guid agentId) => $"{KeyPrefix}conn:{agentId}";

  public Task BindAsync(Guid agentId, string connectionId, string instanceId, CancellationToken cancellationToken = default) =>
    ExecuteAsync(database => database.HashSetAsync(Key(agentId), new HashEntry[]
    {
      new("connectionId", connectionId),
      new("instanceId", instanceId),
      new("connectedAt", timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
    }), cancellationToken);

  public Task UnbindAsync(Guid agentId, string connectionId, CancellationToken cancellationToken = default) =>
    ExecuteAsync(
      database => database.ScriptEvaluateAsync(UnbindScript, new RedisKey[] { Key(agentId) }, new RedisValue[] { connectionId }),
      cancellationToken);

  public Task<ConnectionInfo?> ResolveAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    ExecuteAsync<ConnectionInfo?>(async database =>
    {
      var values = await database.HashGetAsync(Key(agentId), new RedisValue[] { "connectionId", "instanceId" });
      return values[0].HasValue && values[1].HasValue
        ? new ConnectionInfo(values[0].ToString(), values[1].ToString())
        : null;
    }, cancellationToken);

  public Task PurgeInstanceAsync(string instanceId, CancellationToken cancellationToken = default) =>
    ExecuteAsync(async database =>
    {
      var servers = Multiplexer.GetServers().Where(server => server.IsConnected && !server.IsReplica).ToList();
      if (servers.Count == 0)
        throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "No connected Redis server is available.");

      // SCAN in pages, never a blocking KEYS call.
      foreach (var server in servers)
        await foreach (var key in server.KeysAsync(pattern: $"{KeyPrefix}conn:*", pageSize: 250))
          await database.ScriptEvaluateAsync(PurgeScript, new[] { key }, new RedisValue[] { instanceId });
    }, cancellationToken);
}

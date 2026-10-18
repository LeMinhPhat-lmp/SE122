using System.Globalization;
using Application.Interfaces.Runtime;
using Application.Models;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// Passive presence tracking (Protocol Spec section 2.1):
///   {prefix}agent:{id}:alive    = "1"                expires after the offline threshold
///   {prefix}agent:{id}:lastbeat = ISO-8601 UTC time  expires after 24 hours
/// </summary>
public sealed class RedisAgentPresenceStore(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
  : RedisStoreBase(redis, options), IAgentPresenceStore
{
  private static readonly TimeSpan LastBeatTtl = TimeSpan.FromHours(24);

  // The key format must stay identical across write, read and delete, so it is defined once.
  private string AliveKey(Guid agentId) => $"{KeyPrefix}agent:{agentId}:alive";
  private string LastBeatKey(Guid agentId) => $"{KeyPrefix}agent:{agentId}:lastbeat";

  public Task MarkAliveAsync(Guid agentId, DateTimeOffset now, TimeSpan aliveTtl, CancellationToken cancellationToken = default) =>
    ExecuteAsync(async database =>
    {
      await database.StringSetAsync(AliveKey(agentId), "1", aliveTtl);
      await database.StringSetAsync(LastBeatKey(agentId), now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture), LastBeatTtl);
    }, cancellationToken);

  public Task<AgentPresence> GetAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    ExecuteAsync(async database =>
    {
      var isOnline = await database.KeyExistsAsync(AliveKey(agentId));
      var lastBeat = await database.StringGetAsync(LastBeatKey(agentId));

      DateTimeOffset? lastHeartbeatAt = null;
      if (lastBeat.HasValue
          && DateTimeOffset.TryParse(lastBeat.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
      {
        lastHeartbeatAt = parsed;
      }

      return new AgentPresence(isOnline, lastHeartbeatAt);
    }, cancellationToken);

  public Task RemoveAsync(Guid agentId, CancellationToken cancellationToken = default) =>
    ExecuteAsync(
      database => database.KeyDeleteAsync(new RedisKey[] { AliveKey(agentId), LastBeatKey(agentId) }),
      cancellationToken);
}

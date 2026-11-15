using Application.Interfaces.Runtime;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// Publishes the id of a revoked agent on {prefix}agent:revoked (ADR-W8-03). Every instance subscribes, and the one that holds the
/// agent's connection aborts it. Redis pub/sub does not store messages: if the publish is lost, the active-agent check on every
/// hub message is the second line of defense.
/// </summary>
public sealed class RedisConnectionTerminator(IConnectionMultiplexer redis, IOptions<RedisOptions> options, ILogger<RedisConnectionTerminator> logger)
  : RedisStoreBase(redis, options), IConnectionTerminator
{
  public static RedisChannel Channel(string keyPrefix) => RedisChannel.Literal($"{keyPrefix}agent:revoked");

  public async Task TerminateAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    try
    {
      await ExecuteAsync(_ => Multiplexer.GetSubscriber().PublishAsync(Channel(KeyPrefix), agentId.ToString()), cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not publish the revocation of agent {AgentId}.", agentId);
    }
  }
}

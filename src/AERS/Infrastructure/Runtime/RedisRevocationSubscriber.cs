using Application.Interfaces.Runtime;
using Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>Subscribes to {prefix}agent:revoked (ADR-W8-03). A message that is not an agent id is ignored with a warning that carries no payload.</summary>
public sealed class RedisRevocationSubscriber(IConnectionMultiplexer redis, IOptions<RedisOptions> options, ILogger<RedisRevocationSubscriber> logger)
  : RedisStoreBase(redis, options), IRevocationSubscriber
{
  private readonly object gate = new();
  private Action<RedisChannel, RedisValue>? handler;

  public Task SubscribeAsync(Action<Guid> onRevoked, CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();

    Action<RedisChannel, RedisValue> subscription = (_, message) =>
    {
      if (!Guid.TryParse(message.ToString(), out var agentId))
      {
        logger.LogWarning("Ignored a malformed message on the agent revocation channel.");
        return;
      }

      try
      {
        onRevoked(agentId);
      }
      catch (Exception ex)
      {
        logger.LogWarning(ex, "Handling the revocation of agent {AgentId} failed.", agentId);
      }
    };

    lock (gate)
      handler = subscription;

    return Multiplexer.GetSubscriber().SubscribeAsync(RedisConnectionTerminator.Channel(KeyPrefix), subscription);
  }

  public Task UnsubscribeAsync(CancellationToken cancellationToken = default)
  {
    Action<RedisChannel, RedisValue>? current;
    lock (gate)
    {
      current = handler;
      handler = null;
    }

    return current is null
      ? Task.CompletedTask
      : Multiplexer.GetSubscriber().UnsubscribeAsync(RedisConnectionTerminator.Channel(KeyPrefix), current);
  }
}

using Application.Interfaces.Policies;
using Application.Models;
using Application.Options;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// Fixed window counter per caller agent (A56, D-17): key {prefix}ratelimit:{callerAgentId}:{windowStartUnixSeconds}.
/// INCR and EXPIRE run in one Lua script, so the key never exists without a TTL. A denied request still increments the counter
/// (a denied burst does not reopen the window early); Remaining is clamped at zero. This class never decides what happens when
/// Redis is down: the base class throws StateStoreUnavailableException and TaskService applies ADR-W8-01 (fail-closed).
/// </summary>
public sealed class RedisRateLimiter(
  IConnectionMultiplexer redis,
  IOptions<RedisOptions> options,
  IOptions<AersPolicyOptions> policyOptions,
  TimeProvider timeProvider) : RedisStoreBase(redis, options), IRateLimiter
{
  private const string IncrementScript = "local c = redis.call('INCR', KEYS[1]) if c == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end return c";

  public async Task<RateLimitDecision> TryAcquireAsync(Guid callerAgentId, CancellationToken cancellationToken = default)
  {
    var limits = policyOptions.Value.RateLimit;
    var windowStart = timeProvider.GetUtcNow().ToUnixTimeSeconds() / limits.WindowSeconds * limits.WindowSeconds;
    var key = $"{KeyPrefix}ratelimit:{callerAgentId}:{windowStart}";

    var count = (long)await ExecuteAsync(
      database => database.ScriptEvaluateAsync(IncrementScript, new RedisKey[] { key }, new RedisValue[] { limits.WindowSeconds + 1 }),
      cancellationToken);

    return new RateLimitDecision(
      count <= limits.MaxTasksPerWindow,
      limits.MaxTasksPerWindow,
      (int)Math.Max(0, limits.MaxTasksPerWindow - count),
      DateTimeOffset.FromUnixTimeSeconds(windowStart + limits.WindowSeconds));
  }
}

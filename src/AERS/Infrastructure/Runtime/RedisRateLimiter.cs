using System.Globalization;
using Application.Interfaces.Policies;
using Application.Models;
using Application.Options;
using Infrastructure.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Infrastructure.Runtime;

/// <summary>
/// PROVISIONAL (ADR-W8-01): a Redis failure is surfaced to the caller, which applies
/// the accepted fail-closed task-create behavior. REST endpoints are not rate limited.
/// </summary>
public sealed class RedisRateLimiter(
  IConnectionMultiplexer redis,
  IOptions<RedisOptions> redisOptions,
  IOptions<AersPolicyOptions> policyOptions,
  TimeProvider timeProvider)
  : RedisStoreBase(redis, redisOptions), IRateLimiter
{
  private const string AcquireScript = """
    local count = redis.call('INCR', KEYS[1])
    local ttl = redis.call('TTL', KEYS[1])
    if count == 1 or ttl < 0 then
      redis.call('EXPIRE', KEYS[1], ARGV[1])
    end
    return count
    """;

  public Task<RateLimitDecision> TryAcquireAsync(
    Guid callerAgentId,
    CancellationToken cancellationToken = default)
  {
    var options = policyOptions.Value.RateLimit;
    var now = timeProvider.GetUtcNow();
    var unixSeconds = now.ToUnixTimeSeconds();
    var windowStartUnixSeconds = unixSeconds - (unixSeconds % options.WindowSeconds);
    var resetAt = DateTimeOffset.FromUnixTimeSeconds(windowStartUnixSeconds + options.WindowSeconds);
    var key = $"{KeyPrefix}ratelimit:{callerAgentId:D}:{windowStartUnixSeconds.ToString(CultureInfo.InvariantCulture)}";

    return ExecuteAsync(async database =>
    {
      var result = await database.ScriptEvaluateAsync(
        AcquireScript,
        new RedisKey[] { key },
        new RedisValue[] { options.WindowSeconds + 1 });
      var count = checked((int)(long)result);
      var remaining = Math.Max(options.MaxTasksPerWindow - count, 0);

      // Denied calls intentionally increment the fixed-window counter; they must not reopen the window early.
      return new RateLimitDecision(
        count <= options.MaxTasksPerWindow,
        options.MaxTasksPerWindow,
        remaining,
        resetAt);
    }, cancellationToken);
  }
}

using Application.Interfaces.Policies;
using Infrastructure.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Xunit;

namespace AERS.UnitTests;

[Collection("Discovery integration")]
public sealed class RateLimiterIntegrationTests(DiscoveryIntegrationFixture fixture)
{
  [SkippableFact]
  public async Task FiftyConcurrentAcquisitionsAllowExactlyTenAndSetTtl()
  {
    IntegrationTestSkip.Require();
    var callerId = Guid.NewGuid();

    using var scope = fixture.Factory.Services.CreateScope();
    var limiter = scope.ServiceProvider.GetRequiredService<IRateLimiter>();
    var decisions = await Task.WhenAll(
      Enumerable.Range(0, 50).Select(_ => limiter.TryAcquireAsync(callerId)));

    Assert.Equal(10, decisions.Count(decision => decision.Allowed));
    Assert.All(decisions.Where(decision => !decision.Allowed), decision => Assert.Equal(0, decision.Remaining));

    var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
    var redisOptions = scope.ServiceProvider.GetRequiredService<IOptions<RedisOptions>>().Value;
    var windowStart = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    windowStart -= windowStart % 60;
    var key = $"{redisOptions.KeyPrefix}ratelimit:{callerId:D}:{windowStart}";
    var ttl = await redis.GetDatabase().KeyTimeToLiveAsync(key);
    Assert.True(ttl.HasValue && ttl.Value > TimeSpan.Zero && ttl.Value <= TimeSpan.FromSeconds(61));
  }
}

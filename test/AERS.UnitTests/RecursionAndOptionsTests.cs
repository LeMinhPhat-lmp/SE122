using API.Authentication;
using Application.Constants;
using Application.Options;
using Application.Policies;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class RecursionAndOptionsTests
{
  private static TaskPolicyEvaluator Evaluator(int maxDepth) =>
    new(new FakeDomainAccessPolicy((caller, target) => caller == target), new FakeSchemaValidator(),
      Microsoft.Extensions.Options.Options.Create(new AersPolicyOptions { MaxRecursionDepth = maxDepth }));

  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(5)]
  public void Depth_equal_to_the_maximum_is_accepted_and_one_more_is_rejected(int max)
  {
    var evaluator = Evaluator(max);
    var target = Guid.NewGuid();

    Assert.True(evaluator.EvaluateRecursion(target, max, []).IsSuccess);
    Assert.Equal(ErrorCodes.DepthExceeded, evaluator.EvaluateRecursion(target, max + 1, []).ErrorCode);
  }

  [Fact]
  public void Cycle_table_follows_the_ancestor_chain_and_depth_wins_when_both_apply()
  {
    var evaluator = Evaluator(5);
    Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid(), d = Guid.NewGuid();

    Assert.True(evaluator.EvaluateRecursion(b, 0, [a]).IsSuccess);           // A to B
    Assert.Equal(ErrorCodes.CycleDetected, evaluator.EvaluateRecursion(a, 0, [a]).ErrorCode); // A to A
    Assert.True(evaluator.EvaluateRecursion(c, 1, [a, b]).IsSuccess);        // B to C
    Assert.Equal(ErrorCodes.CycleDetected, evaluator.EvaluateRecursion(a, 2, [a, b, c]).ErrorCode); // C to A
    Assert.Equal(ErrorCodes.CycleDetected, evaluator.EvaluateRecursion(b, 2, [a, b, c]).ErrorCode); // C to B
    Assert.True(evaluator.EvaluateRecursion(d, 2, [a, b, c]).IsSuccess);     // C to D
    Assert.True(evaluator.EvaluateRecursion(a, 0, []).IsSuccess);            // empty chain
    Assert.Equal(ErrorCodes.DepthExceeded, evaluator.EvaluateRecursion(a, 6, [a]).ErrorCode);
  }

  [Fact]
  public void Every_new_task_option_rejects_zero_and_negative_values()
  {
    var validator = new TaskOptionsValidator(Microsoft.Extensions.Options.Options.Create(new RelayOptions()));
    Assert.True(validator.Validate(null, new TaskOptions()).Succeeded);

    var invalid = new Action<TaskOptions>[]
    {
      o => o.TimeoutScanIntervalSeconds = 0,
      o => o.TimeoutBatchSize = 0,
      o => o.RuntimeStateGraceSeconds = -1,
      o => o.RuntimeStateTerminalTtlSeconds = 0,
      o => o.RedeliveryBatchSize = 0,
      o => o.TargetDisconnectGraceSeconds = 0
    };

    foreach (var mutate in invalid)
    {
      var options = new TaskOptions();
      mutate(options);
      Assert.False(validator.Validate(null, options).Succeeded);
    }
  }

  [Fact]
  public void Policy_options_reject_a_depth_or_rate_limit_below_one()
  {
    Assert.True(AersPolicyOptionsValidator.IsValid(new AersPolicyOptions()));
    Assert.False(AersPolicyOptionsValidator.IsValid(new AersPolicyOptions { MaxRecursionDepth = 0 }));
    Assert.False(AersPolicyOptionsValidator.IsValid(new AersPolicyOptions { RateLimit = { WindowSeconds = 0 } }));
    Assert.False(AersPolicyOptionsValidator.IsValid(new AersPolicyOptions { RateLimit = { MaxTasksPerWindow = 0 } }));
  }

  [Fact]
  public void Auth_failure_log_is_limited_per_address_and_class_and_bounded()
  {
    var time = new ManualTimeProvider(TestData.BaseTime);
    var limiter = new AuthFailureLogLimiter(time);

    Assert.True(limiter.ShouldLog("10.0.0.1", "unknown_or_revoked"));
    Assert.False(limiter.ShouldLog("10.0.0.1", "unknown_or_revoked"));
    Assert.True(limiter.ShouldLog("10.0.0.2", "unknown_or_revoked"));
    Assert.True(limiter.ShouldLog("10.0.0.1", "missing"));

    time.Advance(TimeSpan.FromSeconds(11));
    Assert.True(limiter.ShouldLog("10.0.0.1", "unknown_or_revoked"));

    for (var i = 0; i < AuthFailureLogLimiter.MaxEntries + 50; i++)
      limiter.ShouldLog($"host-{i}", "missing");
    Assert.True(limiter.Count <= AuthFailureLogLimiter.MaxEntries);
  }
}

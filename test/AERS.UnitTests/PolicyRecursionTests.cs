using Application.Constants;
using Application.Interfaces.Validation;
using Application.Options;
using Application.Policies;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class PolicyRecursionTests
{
  public static IEnumerable<object[]> DepthBoundaries() =>
  [
    [0, 0, true],
    [0, 1, false],
    [1, 1, true],
    [1, 2, false],
    [5, 5, true],
    [5, 6, false]
  ];

  [Theory]
  [MemberData(nameof(DepthBoundaries))]
  public void EvaluateRecursion_UsesMaxDepthBoundary(int maxDepth, int depth, bool expectedSuccess)
  {
    var evaluator = CreateEvaluator(maxDepth);
    var result = evaluator.EvaluateRecursion(Guid.NewGuid(), depth, []);

    Assert.Equal(expectedSuccess, result.IsSuccess);
    if (!expectedSuccess)
      Assert.Equal(ErrorCodes.DepthExceeded, result.ErrorCode);
  }

  [Fact]
  public void EvaluateRecursion_ChecksDepthBeforeCycle()
  {
    var target = Guid.NewGuid();
    var result = CreateEvaluator(5).EvaluateRecursion(target, 6, [target]);

    Assert.Equal(ErrorCodes.DepthExceeded, result.ErrorCode);
  }

  [Theory]
  [InlineData(false, 1, 1)]
  [InlineData(true, 1, 0)]
  [InlineData(false, 2, 2)]
  [InlineData(true, 3, 0)]
  [InlineData(true, 3, 1)]
  [InlineData(false, 3, 3)]
  [InlineData(false, 0, 3)]
  public void EvaluateRecursion_DetectsAgentCycles(bool shouldCycle, int chainLength, int targetIndex)
  {
    var a = Guid.NewGuid();
    var b = Guid.NewGuid();
    var c = Guid.NewGuid();
    var d = Guid.NewGuid();
    var target = chainLength switch
    {
      0 => d,
      1 => targetIndex == 0 ? a : b,
      2 => targetIndex == 0 ? a : targetIndex == 1 ? b : c,
      _ => targetIndex == 0 ? a : targetIndex == 1 ? b : targetIndex == 2 ? c : d
    };

    var chain = chainLength switch
    {
      0 => Array.Empty<Guid>(),
      1 => new[] { a },
      2 => new[] { a, b },
      _ => new[] { a, b, c }
    };
    var result = CreateEvaluator(5).EvaluateRecursion(target, chain.Length, chain);

    Assert.Equal(shouldCycle, result.ErrorCode == ErrorCodes.CycleDetected);

  }

  [Fact]
  public void EvaluateRecursion_IsIndependentOfInputPolicyDependencies()
  {
    var evaluator = CreateEvaluator(5);

    var result = evaluator.EvaluateRecursion(Guid.NewGuid(), 0, []);

    Assert.True(result.IsSuccess);
  }

  private static TaskPolicyEvaluator CreateEvaluator(int maxDepth) =>
    new(
      new FakeDomainAccessPolicy((_, _) => true),
      new ThrowingSchemaValidator(),
      Options.Create(new AersPolicyOptions { MaxRecursionDepth = maxDepth }));

  private sealed class ThrowingSchemaValidator : IInputSchemaValidator
  {
    public IReadOnlyList<Application.DTOs.Result.Responses.ErrorDetail> Validate(
      System.Text.Json.JsonElement schema,
      System.Text.Json.JsonElement instance) =>
      throw new InvalidOperationException("EvaluateRecursion must not inspect input schemas.");
  }
}

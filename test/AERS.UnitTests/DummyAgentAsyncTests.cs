using System.Text.Json;
using AERS.AgentClient;
using AERS.DummyAgent.Core.Behaviors;
using AERS.DummyAgent.Core.Scenarios;
using Xunit;

namespace AERS.UnitTests;

public sealed class DummyAgentAsyncTests
{
  [Fact]
  public async Task Slow_CompletesWithEchoWithoutBlockingCaller()
  {
    var input = JsonDocument.Parse("""{"message":"hello"}""").RootElement.Clone();
    var handler = DummyCatalog.Create(new("slow", DelayMilliseconds: 1));

    var result = await handler(new TaskInvocation(Guid.NewGuid(), Guid.NewGuid(), null, 0, "slow", input, null));

    Assert.Equal("completed", result.Status);
    Assert.Equal(input, result.Output);
  }

  [Fact]
  public async Task Silent_SuppressesReply()
  {
    var handler = DummyCatalog.Create(new("silent"));

    var result = await handler(new TaskInvocation(Guid.NewGuid(), Guid.NewGuid(), null, 0, "silent",
      JsonDocument.Parse("{}").RootElement.Clone(), null));

    Assert.True(result.SuppressReply);
  }

  [Fact]
  public async Task DropAfterReceive_DisconnectsAndCanReconnectBeforeAnswering()
  {
    var disconnected = false;
    var reconnected = false;
    var handler = DummyCatalog.Create(
      new("drop-after-receive", ReconnectAfterMilliseconds: 1, AnswerAfterReconnect: true),
      _ =>
      {
        disconnected = true;
        return Task.CompletedTask;
      },
      _ =>
      {
        reconnected = true;
        return Task.CompletedTask;
      });

    var result = await handler(new TaskInvocation(Guid.NewGuid(), Guid.NewGuid(), null, 0, "drop-after-receive",
      JsonDocument.Parse("""{"value":1}""").RootElement.Clone(), null));

    Assert.True(disconnected);
    Assert.True(reconnected);
    Assert.Equal("completed", result.Status);
  }

  [Fact]
  public void ScenarioRunner_ReportsSupportedScenarios()
  {
    var runner = new ScenarioRunner();

    Assert.Contains("caller-offline", runner.SupportedScenarios);
    Assert.True(runner.RunDry("cycle").Passed);
    Assert.False(runner.RunDry("unknown").Passed);
  }
}

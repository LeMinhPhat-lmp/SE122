using System.Text.Json;
using AERS.AgentClient;

namespace AERS.DummyAgent.Core.Behaviors;

public sealed record DummyBehaviorOptions(
  string Name,
  int DelayMilliseconds = 0,
  int ReconnectAfterMilliseconds = 0,
  bool AnswerAfterReconnect = false,
  Guid? NextAgentId = null,
  string NextCapability = "echo");

public static class DummyCatalog
{
  public static Func<TaskInvocation, Task<InvocationResult>> Create(
    DummyBehaviorOptions options,
    Func<CancellationToken, Task>? disconnect = null,
    Func<CancellationToken, Task>? reconnect = null,
    Func<TaskInvocation, CancellationToken, Task<InvocationResult>>? childCall = null)
  {
    ArgumentNullException.ThrowIfNull(options);
    return options.Name switch
    {
      "echo" => invocation => Task.FromResult(Completed(invocation.Input)),
      "slow" => invocation => SlowAsync(invocation, options.DelayMilliseconds),
      "silent" => _ => Task.FromResult(new InvocationResult("completed", null, null, SuppressReply: true)),
      "drop-after-receive" => DropAsync,
      "always_fail" => _ => Task.FromResult(new InvocationResult(
        "failed", null, new TaskError("DUMMY_FAILURE", "The dummy behavior failed."))),
      "analyze_cv" => invocation => Task.FromResult(Completed(JsonSerializer.SerializeToElement(new { score = 42 }))),
      "chain" when options.NextAgentId is not null && childCall is not null =>
        invocation => ChainAsync(invocation, options, childCall),
      _ => throw new ArgumentException($"Unknown or incomplete dummy behavior '{options.Name}'.", nameof(options))
    };

    async Task<InvocationResult> DropAsync(TaskInvocation invocation)
    {
      if (disconnect is not null)
        await disconnect(CancellationToken.None);
      if (options.AnswerAfterReconnect && reconnect is not null)
      {
        if (options.ReconnectAfterMilliseconds > 0)
          await Task.Delay(options.ReconnectAfterMilliseconds);
        await reconnect(CancellationToken.None);
        return Completed(invocation.Input);
      }
      return new InvocationResult("completed", null, null, SuppressReply: true);
    }

    static async Task<InvocationResult> SlowAsync(TaskInvocation invocation, int delayMilliseconds)
    {
      if (delayMilliseconds < 0)
        throw new ArgumentOutOfRangeException(nameof(delayMilliseconds));
      await Task.Delay(delayMilliseconds);
      return Completed(invocation.Input);
    }

    static InvocationResult Completed(JsonElement output) =>
      new("completed", output.Clone(), null);
  }

  private static async Task<InvocationResult> ChainAsync(
    TaskInvocation invocation,
    DummyBehaviorOptions options,
    Func<TaskInvocation, CancellationToken, Task<InvocationResult>> childCall)
  {
    var child = await childCall(new TaskInvocation(
      Guid.NewGuid(),
      invocation.RootTaskId,
      invocation.TaskId,
      invocation.Depth + 1,
      options.NextCapability,
      invocation.Input,
      invocation.PolicyContext), CancellationToken.None);
    if (child.Status == "failed")
      return new InvocationResult("failed", null, child.Error);
    return new InvocationResult(
      "completed",
      JsonSerializer.SerializeToElement(new { via = options.Name, child = child.Output }),
      null);
  }
}

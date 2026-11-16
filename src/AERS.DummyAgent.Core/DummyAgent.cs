using AERS.AgentClient;
using AERS.DummyAgent.Core.Behaviors;

namespace AERS.DummyAgent.Core;

public sealed class DummyAgent(
  AersAgentConnection connection,
  DummyBehaviorOptions behavior,
  Guid agentId)
{
  public Guid AgentId { get; } = agentId;
  public string BehaviorName => behavior.Name;
  public List<Guid> Invocations { get; } = [];

  public void Start(
    Func<CancellationToken, Task>? disconnect = null,
    Func<CancellationToken, Task>? reconnect = null,
    Func<TaskInvocation, CancellationToken, Task<InvocationResult>>? childCall = null)
  {
    connection.RegisterInvocationHandler(behavior.Name, async invocation =>
    {
      Invocations.Add(invocation.TaskId);
      return await DummyCatalog.Create(behavior, disconnect, reconnect, childCall)(invocation);
    });
  }

  public Task StopAsync(CancellationToken cancellationToken = default) =>
    connection.StopAsync(cancellationToken);
}

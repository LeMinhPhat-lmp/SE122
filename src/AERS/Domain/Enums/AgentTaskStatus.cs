namespace Domain.Enums;

/// <summary>
/// Lifecycle of a Task. Named AgentTaskStatus so it never clashes with System.Threading.Tasks.TaskStatus.
/// The numeric values are persisted (smallint) and must not change.
/// </summary>
public enum AgentTaskStatus
{
  Submitted = 0,
  Working = 1,
  Completed = 2,
  Failed = 3
}

public static class AgentTaskStatusExtensions
{
  public static bool IsTerminal(this AgentTaskStatus status) =>
    status is AgentTaskStatus.Completed or AgentTaskStatus.Failed;

  /// <summary>
  /// The single place that defines the allowed edges of the lifecycle (decisions A20 and A27):
  /// submitted to working, submitted to failed, working to completed, working to failed.
  /// </summary>
  public static bool CanTransitionTo(this AgentTaskStatus from, AgentTaskStatus to) =>
    (from, to) is (AgentTaskStatus.Submitted, AgentTaskStatus.Working)
      or (AgentTaskStatus.Submitted, AgentTaskStatus.Failed)
      or (AgentTaskStatus.Working, AgentTaskStatus.Completed)
      or (AgentTaskStatus.Working, AgentTaskStatus.Failed);
}

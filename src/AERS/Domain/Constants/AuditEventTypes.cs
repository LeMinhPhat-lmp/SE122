namespace Domain.Constants;

public static class AuditEventTypes
{
  public const string AgentRegistered = "agent.registered";
  public const string AgentDeregistered = "agent.deregistered";
  public const string TaskCreated = "task.created";
  public const string TaskStatusChanged = "task.status_changed";
  public const string PolicyRejected = "policy.rejected";
}

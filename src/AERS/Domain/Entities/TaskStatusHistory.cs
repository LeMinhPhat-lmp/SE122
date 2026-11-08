using Domain.Enums;

namespace Domain.Entities;

/// <summary>One row per status transition after creation. The initial insert writes no history row of its own.</summary>
public sealed class TaskStatusHistory
{
  public long Id { get; private set; }
  public Guid TaskId { get; set; }
  public AgentTaskStatus? FromStatus { get; set; }
  public AgentTaskStatus ToStatus { get; set; }
  public DateTimeOffset ChangedAt { get; set; }

  public static TaskStatusHistory Create(Guid taskId, AgentTaskStatus? fromStatus, AgentTaskStatus toStatus, DateTimeOffset changedAt) => new()
  {
    TaskId = taskId,
    FromStatus = fromStatus,
    ToStatus = toStatus,
    ChangedAt = changedAt
  };
}

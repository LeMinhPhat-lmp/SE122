using System.Text.Json;

namespace Domain.Entities;

public sealed class AuditEvent
{
  public long Id { get; private set; }
  public string EventType { get; set; } = null!;
  public DateTimeOffset OccurredAt { get; set; }

  // No foreign keys on purpose: the audit trail must outlive agents and tasks.
  public Guid? AgentId { get; set; }
  public Guid? TaskId { get; set; }
  public JsonElement? Details { get; set; }

  public static AuditEvent Create(
    string eventType,
    DateTimeOffset occurredAt,
    Guid? agentId = null,
    Guid? taskId = null,
    JsonElement? details = null) => new()
  {
    EventType = eventType,
    OccurredAt = occurredAt,
    AgentId = agentId,
    TaskId = taskId,
    Details = details
  };
}

using System.Text.Json;
using Domain.Abstractions;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// One Agent-to-Agent call (Protocol Spec section 3.2). Status changes are never applied through this entity:
/// they go through ITaskRepository.TryTransitionAsync, a single conditional UPDATE (decision A27).
/// CreatedAt is set explicitly by the service from the injected TimeProvider (not by the DbContext timestamp hook),
/// because the value is returned to clients and must come from the same clock as DeadlineAt.
/// </summary>
public sealed class AgentTask : BaseEntity
{
  public Guid CallerAgentId { get; set; }
  public Guid TargetAgentId { get; set; }
  public string CapabilityName { get; set; } = null!;
  public AgentTaskStatus Status { get; set; }
  public JsonElement Input { get; set; }
  public JsonElement? Output { get; set; }
  public Guid? ParentTaskId { get; set; }
  public Guid RootTaskId { get; set; }
  public int Depth { get; set; }

  /// <summary>Server-side only (Spec section 4). Never part of a wire schema.</summary>
  public List<Guid> AncestorChain { get; set; } = new();

  public DateTimeOffset DeadlineAt { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? StartedAt { get; set; }
  public DateTimeOffset? CompletedAt { get; set; }
  public TaskError? Error { get; set; }

  public static AgentTask Create(
    Guid id,
    Guid callerAgentId,
    Guid targetAgentId,
    string capabilityName,
    JsonElement input,
    Guid? parentTaskId,
    Guid rootTaskId,
    int depth,
    IEnumerable<Guid> ancestorChain,
    DateTimeOffset deadlineAt,
    DateTimeOffset createdAt) => new()
  {
    Id = id,
    CallerAgentId = callerAgentId,
    TargetAgentId = targetAgentId,
    CapabilityName = capabilityName,
    Status = AgentTaskStatus.Submitted,
    Input = input,
    ParentTaskId = parentTaskId,
    RootTaskId = rootTaskId,
    Depth = depth,
    AncestorChain = ancestorChain.ToList(),
    DeadlineAt = deadlineAt,
    CreatedAt = createdAt
  };
}

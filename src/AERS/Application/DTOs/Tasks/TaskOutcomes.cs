using Domain.Enums;

namespace Application.DTOs.Tasks;

/// <summary>Result of ITaskService.DispatchAsync. ResultToPush is set when the Task is already terminal and the caller must be told.</summary>
public sealed record TaskDispatchOutcome(Guid TaskId, AgentTaskStatus Status, ErrorPayload? Error, Guid CallerAgentId, TaskResult? ResultToPush);

/// <summary>Result of ITaskService.ReceiveResultAsync. ResultToForward is the TaskResult to push to the caller agent.</summary>
public sealed record TaskResultOutcome(Guid TaskId, AgentTaskStatus Status, Guid CallerAgentId, TaskResult ResultToForward);

public sealed record TaskStatusEventOutcome(Guid TaskId, AgentTaskStatus Status);

/// <summary>Result of ITaskService.AcknowledgeAsync (ADR-W7-02). Status is the terminal status of the acknowledged task.</summary>
public sealed record TaskAckOutcome(Guid TaskId, AgentTaskStatus Status);

/// <summary>
/// Result of ITaskService.ExpireAsync (A46). <paramref name="Expired"/> is false when nothing changed (not yet due, already
/// terminal, or the compare-and-set lost). <paramref name="ResultToPush"/> is set only when <paramref name="Expired"/> is true.
/// </summary>
public sealed record TaskExpiryOutcome(Guid TaskId, bool Expired, Guid CallerAgentId, TaskResult? ResultToPush);

/// <summary>
/// Result of ITaskService.FailTasksOfDisconnectedTargetAsync (ADR-W7-03). <paramref name="Examined"/> is the number of working
/// tasks that were looked at; when it is smaller than the requested limit the agent has no working task left.
/// </summary>
public sealed record DisconnectFailureBatch(int Examined, IReadOnlyList<TaskExpiryOutcome> Failed);

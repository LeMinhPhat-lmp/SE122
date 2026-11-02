using Domain.Enums;

namespace Application.DTOs.Tasks;

/// <summary>Result of ITaskService.DispatchAsync. ResultToPush is set when the Task is already terminal and the caller must be told.</summary>
public sealed record TaskDispatchOutcome(Guid TaskId, AgentTaskStatus Status, ErrorPayload? Error, Guid CallerAgentId, TaskResult? ResultToPush);

/// <summary>Result of ITaskService.ReceiveResultAsync. ResultToForward is the TaskResult to push to the caller agent.</summary>
public sealed record TaskResultOutcome(Guid TaskId, AgentTaskStatus Status, Guid CallerAgentId, TaskResult ResultToForward);

public sealed record TaskStatusEventOutcome(Guid TaskId, AgentTaskStatus Status);

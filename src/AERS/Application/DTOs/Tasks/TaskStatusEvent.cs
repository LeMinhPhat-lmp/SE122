namespace Application.DTOs.Tasks;

/// <summary>Payload of task.status_event (Protocol Spec section 5.3). In Week 6 only status "working" is accepted and "result" must be null.</summary>
public sealed record TaskStatusEvent(string TaskId, string Status);

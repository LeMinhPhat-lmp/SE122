namespace Application.DTOs.Tasks;

/// <summary>Payload of task.ack, client to server (ADR-W7-02): the caller confirms it processed the terminal result of a task.</summary>
public sealed record TaskAck(string TaskId);

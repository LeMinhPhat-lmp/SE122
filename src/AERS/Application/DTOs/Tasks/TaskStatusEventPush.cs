namespace Application.DTOs.Tasks;

/// <summary>
/// Payload of the task.status_event that AERS pushes to a caller (A41). All three members are always serialized:
/// "result" is explicit null because only terminal states carry a result, and those are pushed as task.result.
/// </summary>
public sealed record TaskStatusEventPush(string TaskId, string Status, TaskResult? Result);

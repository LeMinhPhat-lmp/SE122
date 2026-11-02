namespace Application.DTOs.Tasks;

/// <summary>Return value of the hub method Send (decision A24). All four members are always present, with explicit null.</summary>
public sealed record EnvelopeAck(bool Accepted, string? TaskId, string? Status, ErrorPayload? Error);

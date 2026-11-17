namespace Application.Interfaces.Runtime;

/// <summary>
/// Tells every AERS instance that an agent was revoked so that the instance holding its hub connection aborts it
/// (ADR-W8-03: Redis pub/sub channel {prefix}agent:revoked). Best effort: it never throws except on cancellation.
/// </summary>
public interface IConnectionTerminator
{
  Task TerminateAsync(Guid agentId, CancellationToken cancellationToken = default);
}

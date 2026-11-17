namespace Application.Interfaces.Features;

/// <summary>
/// Redelivers terminal results that a caller has not acknowledged (A49, ADR-W7-02). At least once: a result can arrive more than
/// once, so clients treat task.result as idempotent per taskId and send task.ack after processing it. Never throws except
/// <see cref="OperationCanceledException"/>.
/// </summary>
public interface ITaskRecoveryService
{
  Task RedeliverAsync(Guid agentId, CancellationToken cancellationToken = default);
}

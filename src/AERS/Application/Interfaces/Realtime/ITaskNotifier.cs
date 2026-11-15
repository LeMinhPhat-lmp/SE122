using Application.DTOs.Tasks;

namespace Application.Interfaces.Realtime;

/// <summary>
/// Pushes results and working events to a caller (A41, ADR-W7-02). Pushes are best effort: a caller without a connection, or a
/// failed send, is only logged. A result that was not delivered stays unacknowledged in PostgreSQL and is redelivered when the
/// caller reconnects. Neither method throws except <see cref="OperationCanceledException"/>.
/// </summary>
public interface ITaskNotifier
{
  Task NotifyResultAsync(Guid callerAgentId, TaskResult result, CancellationToken cancellationToken = default);

  Task NotifyWorkingAsync(Guid callerAgentId, Guid taskId, CancellationToken cancellationToken = default);
}

using Application.DTOs.Tasks;
using Application.Models;

namespace Application.Interfaces.Realtime;

public interface IMessageRelay
{
  /// <summary>
  /// Calls the client method "Receive" on the given connection. Throws on failure; callers translate that into
  /// TARGET_UNAVAILABLE or a logged warning. SignalR does not report unknown connection ids, so detecting an
  /// unavailable target relies on the connection registry being correct (decision A31).
  /// </summary>
  Task SendAsync(ConnectionInfo target, TaskEnvelope envelope, CancellationToken cancellationToken = default);
}

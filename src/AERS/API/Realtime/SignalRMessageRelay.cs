using Application.DTOs.Tasks;
using Application.Interfaces.Realtime;
using Application.Models;
using Microsoft.AspNetCore.SignalR;

namespace API.Realtime;

/// <summary>
/// Delivers an envelope to one connection by calling the client method "Receive". Exceptions propagate.
/// SignalR does NOT report an unknown connection id: the message is silently dropped. Detecting an unavailable target
/// therefore relies on the connection registry being correct, which is why unbind is compare-and-delete and why every
/// mapping of this instance is purged on startup (decision A31).
/// </summary>
public sealed class SignalRMessageRelay(IHubContext<AgentHub> hubContext) : IMessageRelay
{
  public const string ClientMethod = "Receive";

  public Task SendAsync(Application.Models.ConnectionInfo target, TaskEnvelope envelope, CancellationToken cancellationToken = default) =>
    hubContext.Clients.Client(target.ConnectionId).SendAsync(ClientMethod, envelope, cancellationToken);
}

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace API.Realtime;

/// <summary>
/// Hub connections that are open on THIS instance, by agent id (ADR-W8-03). The hub adds a context on connect and removes it on
/// disconnect, so the dictionary cannot leak. It holds one context per agent: last connection wins, like the connection registry.
/// </summary>
public sealed class LiveConnectionTracker
{
  private readonly ConcurrentDictionary<Guid, HubCallerContext> connections = new();

  public void Add(Guid agentId, HubCallerContext context) => connections[agentId] = context;

  /// <summary>Compare-and-remove on the connection id: an old connection closing never removes a newer one.</summary>
  public void Remove(Guid agentId, string connectionId)
  {
    if (connections.TryGetValue(agentId, out var current) && current.ConnectionId == connectionId)
      connections.TryRemove(new KeyValuePair<Guid, HubCallerContext>(agentId, current));
  }

  /// <summary>Aborts the agent's connection when this instance holds it. Returns whether a connection was aborted.</summary>
  public bool Abort(Guid agentId)
  {
    if (!connections.TryGetValue(agentId, out var context))
      return false;

    context.Abort();
    return true;
  }

  public int Count => connections.Count;
}

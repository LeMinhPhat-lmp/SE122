using API.Authentication;
using Application.DTOs.Tasks;
using Application.Exceptions;
using Application.Interfaces.Features;
using Application.Interfaces.Runtime;
using Application.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace API.Realtime;

/// <summary>
/// SignalR hub at /hubs/agent (decisions A21 and A22). A thin adapter: all logic is in ITaskMessageService.
/// Authentication uses the existing ApiKey scheme (header X-Api-Key on the negotiate and transport requests); a missing,
/// wrong, revoked or registration key is rejected with 401 before the connection exists. The key is never read from the
/// query string and never logged.
/// </summary>
[Authorize(AuthenticationSchemes = AuthSchemes.ApiKey)]
public sealed class AgentHub(
  ITaskMessageService messageService,
  IConnectionRegistry connectionRegistry,
  IOptions<InstanceOptions> instanceOptions,
  ILogger<AgentHub> logger) : Hub
{
  /// <summary>
  /// Client to server. The token is deliberately not Context.ConnectionAborted: a caller that disconnects in the middle of a
  /// task.create must not leave a task half-created (inserted but never dispatched).
  /// </summary>
  public Task<EnvelopeAck> Send(TaskEnvelope envelope) =>
    messageService.HandleAsync(envelope, Context.User!.ToAgentContext(), CancellationToken.None);

  public override async Task OnConnectedAsync()
  {
    var agent = Context.User!.ToAgentContext();
    if (agent.AgentId is not { } agentId)
    {
      Context.Abort();
      return;
    }

    try
    {
      await connectionRegistry.BindAsync(agentId, Context.ConnectionId, instanceOptions.Value.Id);
    }
    catch (StateStoreUnavailableException ex)
    {
      // Never stay connected without a mapping: the agent could not receive anything.
      logger.LogError(ex, "Could not register the connection of agent {AgentId}; aborting it.", agentId);
      Context.Abort();
      return;
    }

    await base.OnConnectedAsync();
  }

  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    if (Context.User!.ToAgentContext().AgentId is { } agentId)
    {
      try
      {
        // Compare-and-delete: a newer connection of the same agent keeps its mapping.
        await connectionRegistry.UnbindAsync(agentId, Context.ConnectionId);
      }
      catch (StateStoreUnavailableException ex)
      {
        logger.LogWarning(ex, "Could not remove the connection mapping of agent {AgentId}.", agentId);
      }
    }

    await base.OnDisconnectedAsync(exception);
  }
}

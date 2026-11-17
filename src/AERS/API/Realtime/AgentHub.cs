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
  IDisconnectTracker disconnectTracker,
  LiveConnectionTracker liveConnections,
  IServiceScopeFactory scopeFactory,
  IHostApplicationLifetime applicationLifetime,
  IOptions<InstanceOptions> instanceOptions,
  TimeProvider timeProvider,
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

    liveConnections.Add(agentId, Context);

    try
    {
      // ADR-W7-03: the agent is back, so a pending disconnect mark no longer applies.
      await disconnectTracker.ClearAsync(agentId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not clear the disconnect mark of agent {AgentId}.", agentId);
    }

    await base.OnConnectedAsync();

    // A49 / ADR-W7-02: redeliver unacknowledged results OUTSIDE the connect path. The hub's own DI scope ends with this
    // invocation, so the work gets a new scope. The application stopping token (not CancellationToken.None) is used so that a
    // shutdown does not wait for it; an abandoned pass loses nothing because nothing is deleted before the caller acknowledges.
    var stopping = applicationLifetime.ApplicationStopping;
    _ = Task.Run(async () =>
    {
      try
      {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ITaskRecoveryService>().RedeliverAsync(agentId, stopping);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogWarning(ex, "Redelivery for agent {AgentId} failed.", agentId);
      }
    });
  }

  public override async Task OnDisconnectedAsync(Exception? exception)
  {
    if (Context.User!.ToAgentContext().AgentId is { } agentId)
    {
      liveConnections.Remove(agentId, Context.ConnectionId);

      try
      {
        // Compare-and-delete: a newer connection of the same agent keeps its mapping.
        var unbound = await connectionRegistry.UnbindAsync(agentId, Context.ConnectionId);

        // A45 / ADR-W7-03: nothing is failed here. A target may reconnect and still answer, and a caller disconnecting does not
        // affect its tasks. Only when this was the current connection is the time of the disconnect recorded; the timeout
        // worker fails the target's working tasks if it has not reconnected after TargetDisconnectGraceSeconds.
        if (unbound)
          await disconnectTracker.MarkDisconnectedAsync(agentId, timeProvider.GetUtcNow());
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogWarning(ex, "Could not remove the connection mapping or record the disconnect of agent {AgentId}.", agentId);
      }
    }

    await base.OnDisconnectedAsync(exception);
  }
}

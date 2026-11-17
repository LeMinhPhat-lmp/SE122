using Application.Interfaces.Runtime;

namespace API.Realtime;

/// <summary>
/// Every instance subscribes to the revocation channel and aborts the hub connection of a revoked agent if it holds it
/// (ADR-W8-03). If Redis is not reachable at startup the subscription is retried every few seconds.
/// </summary>
public sealed class ConnectionTerminationSubscriber(
  IRevocationSubscriber subscriber,
  LiveConnectionTracker liveConnections,
  ILogger<ConnectionTerminationSubscriber> logger) : BackgroundService
{
  private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    try
    {
      while (true)
      {
        try
        {
          await subscriber.SubscribeAsync(OnRevoked, stoppingToken);
          break;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          logger.LogWarning(ex, "Could not subscribe to the agent revocation channel; retrying.");
          await Task.Delay(RetryDelay, stoppingToken);
        }
      }

      await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    catch (OperationCanceledException)
    {
      // Stopping.
    }
  }

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    try
    {
      await subscriber.UnsubscribeAsync(cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not unsubscribe from the agent revocation channel.");
    }

    await base.StopAsync(cancellationToken);
  }

  private void OnRevoked(Guid agentId)
  {
    if (liveConnections.Abort(agentId))
      logger.LogInformation("Aborted the hub connection of revoked agent {AgentId}.", agentId);
  }
}

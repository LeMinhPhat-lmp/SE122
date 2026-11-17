using System.Text.Json;
using Application.Constants;
using Application.DTOs.Tasks;
using Application.Interfaces.Features;
using Application.Interfaces.Realtime;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Mappings;
using Application.Options;
using Application.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services;

/// <summary>
/// Redelivery of unacknowledged terminal results when a caller reconnects (A49, ADR-W7-02). Nothing is deleted by delivering:
/// a task keeps being redelivered until the caller sends task.ack, so delivery is at least once and a result may arrive twice
/// (a live push racing with the redelivery, or two quick reconnects). Clients dedupe by taskId. Working tasks, task.invoke and
/// task.status_event are never replayed.
/// </summary>
public sealed class TaskRecoveryService(
  IUnitOfWork unitOfWork,
  IConnectionRegistry connectionRegistry,
  IMessageRelay messageRelay,
  IOptions<TaskOptions> taskOptions,
  ILogger<TaskRecoveryService> logger) : ITaskRecoveryService
{
  public async Task RedeliverAsync(Guid agentId, CancellationToken cancellationToken = default)
  {
    try
    {
      var tasks = await unitOfWork.TaskRepository.ListUnacknowledgedTerminalAsync(agentId, taskOptions.Value.RedeliveryBatchSize, cancellationToken);
      if (tasks.Count == 0)
        return;

      // The connection that triggered this call is the current one. If it is gone, the next connect redelivers again.
      var connection = await connectionRegistry.ResolveAsync(agentId, cancellationToken);
      if (connection is null)
        return;

      var delivered = 0;
      foreach (var task in tasks)
      {
        var envelope = new TaskEnvelope(TaskMessageTypes.Result, null, JsonSerializer.SerializeToElement(task.ToResult(), EnvelopeJson.Options));
        await messageRelay.SendAsync(connection, envelope, cancellationToken);
        delivered++;
      }

      logger.LogInformation("Redelivered {Delivered} unacknowledged results to agent {AgentId}.", delivered, agentId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // A failed send ends the pass; nothing was deleted, so the next reconnect tries again.
      logger.LogWarning(ex, "Redelivery to agent {AgentId} stopped early.", agentId);
    }
  }
}

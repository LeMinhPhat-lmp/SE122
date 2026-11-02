using System.Text.Json;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;
using Application.Interfaces.Features;
using Application.Interfaces.Realtime;
using Application.Interfaces.Runtime;
using Application.Mappings;
using Application.Models;
using Application.Tasks;
using Microsoft.Extensions.Logging;

namespace Application.Services;

public sealed class TaskMessageService(
  ITaskService taskService,
  IConnectionRegistry connectionRegistry,
  IMessageRelay messageRelay,
  ILogger<TaskMessageService> logger) : ITaskMessageService
{
  public async Task<EnvelopeAck> HandleAsync(TaskEnvelope envelope, AuthenticatedAgentContext sender, CancellationToken cancellationToken = default)
  {
    try
    {
      if (sender.AgentId is null)
        return Reject(Result.Failure(ResultStatus.Unauthorized, ErrorCodes.AuthenticationFailed, "Authentication failed."));

      var invalid = ValidateEnvelope(envelope);
      if (invalid is not null)
        return Reject(invalid);

      return envelope.MessageType switch
      {
        TaskMessageTypes.Create => await HandleCreateAsync(envelope.Payload, sender, cancellationToken),
        TaskMessageTypes.Result => await HandleResultAsync(envelope.Payload, sender, cancellationToken),
        _ => await HandleStatusEventAsync(envelope.Payload, sender, cancellationToken)
      };
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      // Decision A38: the connection stays open and nothing from the envelope is logged.
      logger.LogError(ex, "Unexpected error while handling a task envelope.");
      return new EnvelopeAck(false, null, null, new ErrorPayload(ErrorCodes.InternalError, "An unexpected error occurred."));
    }
  }

  private async Task<EnvelopeAck> HandleCreateAsync(JsonElement payload, AuthenticatedAgentContext sender, CancellationToken cancellationToken)
  {
    var parsed = EnvelopePayloadParser.ParseCreate(payload);
    if (!parsed.IsSuccess)
      return Reject(parsed);

    var created = await taskService.CreateAsync(parsed.Data!, sender, cancellationToken);
    if (!created.IsSuccess)
      return Reject(created);

    var dispatched = await taskService.DispatchAsync(created.Data!, cancellationToken);
    if (!dispatched.IsSuccess)
      return Reject(dispatched);

    var outcome = dispatched.Data!;
    if (outcome.ResultToPush is not null)
      await PushResultAsync(outcome.CallerAgentId, outcome.ResultToPush, cancellationToken);

    return new EnvelopeAck(true, outcome.TaskId.ToString(), outcome.Status.ToWire(), outcome.Error);
  }

  private async Task<EnvelopeAck> HandleResultAsync(JsonElement payload, AuthenticatedAgentContext sender, CancellationToken cancellationToken)
  {
    var parsed = EnvelopePayloadParser.ParseResult(payload);
    if (!parsed.IsSuccess)
      return Reject(parsed);

    var received = await taskService.ReceiveResultAsync(parsed.Data!, sender, cancellationToken);
    if (!received.IsSuccess)
      return Reject(received);

    // Best effort: the target's ack is never changed by what happens to the caller's push (decision A29).
    var outcome = received.Data!;
    await PushResultAsync(outcome.CallerAgentId, outcome.ResultToForward, cancellationToken);

    return new EnvelopeAck(true, outcome.TaskId.ToString(), outcome.Status.ToWire(), null);
  }

  private async Task<EnvelopeAck> HandleStatusEventAsync(JsonElement payload, AuthenticatedAgentContext sender, CancellationToken cancellationToken)
  {
    var parsed = EnvelopePayloadParser.ParseStatusEvent(payload);
    if (!parsed.IsSuccess)
      return Reject(parsed);

    var received = await taskService.ReceiveStatusEventAsync(parsed.Data!, sender, cancellationToken);
    if (!received.IsSuccess)
      return Reject(received);

    var outcome = received.Data!;
    return new EnvelopeAck(true, outcome.TaskId.ToString(), outcome.Status.ToWire(), null);
  }

  private async Task PushResultAsync(Guid agentId, TaskResult result, CancellationToken cancellationToken)
  {
    try
    {
      var connection = await connectionRegistry.ResolveAsync(agentId, cancellationToken);
      if (connection is null)
      {
        logger.LogInformation("Agent {AgentId} has no connection; the result of task {TaskId} stays available through GET /tasks.", agentId, result.TaskId);
        return;
      }

      var envelope = new TaskEnvelope(TaskMessageTypes.Result, null, JsonSerializer.SerializeToElement(result, EnvelopeJson.Options));
      await messageRelay.SendAsync(connection, envelope, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Could not push the result of task {TaskId} to agent {AgentId}.", result.TaskId, agentId);
    }
  }

  // Known type, a direction clients may use, and an object payload (decisions A22, A23, A37).
  private static Result? ValidateEnvelope(TaskEnvelope? envelope)
  {
    ErrorDetail? problem = null;

    if (envelope is null)
      problem = new ErrorDetail("", "The envelope is required.");
    else if (string.IsNullOrEmpty(envelope.MessageType))
      problem = new ErrorDetail("/messageType", "messageType is required.");
    else if (!TaskMessageTypes.All.Contains(envelope.MessageType))
      problem = new ErrorDetail("/messageType", "Unknown message type.");
    else if (!TaskMessageTypes.ClientToServer.Contains(envelope.MessageType))
      problem = new ErrorDetail("/messageType", "This message type cannot be sent by an agent.");
    else if (envelope.Payload.ValueKind == JsonValueKind.Undefined)
      problem = new ErrorDetail("/payload", "payload is required.");
    else if (envelope.Payload.ValueKind != JsonValueKind.Object)
      problem = new ErrorDetail("/payload", "payload must be an object.");

    return problem is null
      ? null
      : Result.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.", [problem]);
  }

  // The one place where a failed Result becomes a rejected ack.
  private static EnvelopeAck Reject(Result failure) => new(false, null, null, failure.ToErrorPayload());
}

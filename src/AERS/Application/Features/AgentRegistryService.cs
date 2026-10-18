using Application.Constants;
using Application.DTOs.Agent.Requests;
using Application.DTOs.Agent.Responses;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.Exceptions;
using Application.Extensions;
using Application.Interfaces.Features;
using Application.Interfaces.Repositories;
using Application.Interfaces.Runtime;
using Application.Interfaces.Security;
using Application.Mappings;
using Application.Models;
using Application.Options;
using Domain.Constants;
using Domain.Entities;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Application.Services;

public sealed class AgentRegistryService(
  IUnitOfWork unitOfWork,
  IValidator<AgentRegistrationRequest> registrationValidator,
  IApiKeyService apiKeyService,
  IAgentPresenceStore presenceStore,
  IOptions<AersPolicyOptions> policyOptions,
  TimeProvider timeProvider,
  ILogger<AgentRegistryService> logger) : IAgentRegistryService
{
  // An agent counts as online while its presence key lives, i.e. for the offline threshold after the last heartbeat.
  private readonly TimeSpan presenceTtl = TimeSpan.FromSeconds(policyOptions.Value.Heartbeat.OfflineThresholdSeconds);

  public async Task<Result<AgentRegistrationResponse>> RegisterAsync(AgentRegistrationRequest request, CancellationToken cancellationToken = default)
  {
    // Normalize first, then validate: the domain pattern applies to the normalized value (spec section 1.2.1).
    var card = request.AgentCard with
    {
      Name = request.AgentCard.Name.Trim(),
      Domain = request.AgentCard.Domain.Trim().ToLowerInvariant()
    };

    var validation = await registrationValidator.ValidateAsync(new AgentRegistrationRequest(card), cancellationToken);
    if (!validation.IsValid)
      return validation.ToFailure<AgentRegistrationResponse>();

    var now = timeProvider.GetUtcNow();
    var agentId = Guid.NewGuid(); // UUID v4 issued by AERS, never by the client (D-01)
    var apiKey = apiKeyService.Generate();

    var agent = Agent.Register(agentId, card.Name, card.Domain, card.Description, card.OwnerRef);
    foreach (var capability in card.Capabilities)
      agent.AddCapability(capability.Name, capability.Description, capability.InputSchema, capability.OutputSchema);
    agent.AddCredential(apiKey.Hash);

    // One SaveChanges is one transaction: agent, capabilities, credential and audit event commit together.
    await unitOfWork.AgentRepository.AddAsync(agent, cancellationToken);
    await unitOfWork.AuditEventRepository.AddAsync(AuditEvent.Create(AuditEventTypes.AgentRegistered, now, agentId), cancellationToken);
    await unitOfWork.SaveChangesAsync(cancellationToken);

    // The database has already committed and the apiKey is only ever returned once, so a state store outage must
    // not fail the registration (decision A5). Only this layer knows that, hence the local catch.
    try
    {
      await presenceStore.MarkAliveAsync(agentId, now, presenceTtl, cancellationToken);
    }
    catch (StateStoreUnavailableException ex)
    {
      logger.LogWarning(ex, "Could not write presence for agent {AgentId} after registration; the first heartbeat will restore it.", agentId);
    }

    return Result<AgentRegistrationResponse>.Success(new AgentRegistrationResponse(agentId, apiKey.RawKey));
  }

  // A StateStoreUnavailableException from the presence store is deliberately not caught here: it reaches the
  // global handler and becomes 500 INTERNAL_ERROR, as required by spec section 2.1.
  public async Task<Result> HeartbeatAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default)
  {
    if (caller.AgentId != agentId)
      return Result.Failure(ResultStatus.Forbidden, ErrorCodes.AgentAccessForbidden, "An agent cannot act on behalf of another agent.");

    var agent = await unitOfWork.AgentRepository.GetByIdAsync(agentId, trackChanges: false, cancellationToken);
    if (agent is null)
      return Result.Failure(ResultStatus.NotFound, ErrorCodes.AgentNotFound, "The agent does not exist or has been deregistered.");

    await presenceStore.MarkAliveAsync(agentId, timeProvider.GetUtcNow(), presenceTtl, cancellationToken);
    return Result.Success();
  }

  public async Task<Result> DeregisterAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default)
  {
    if (caller.AgentId != agentId)
      return Result.Failure(ResultStatus.Forbidden, ErrorCodes.AgentAccessForbidden, "An agent cannot act on behalf of another agent.");

    var agent = await unitOfWork.AgentRepository.GetByIdAsync(agentId, trackChanges: true, cancellationToken);
    if (agent is null)
      return Result.Failure(ResultStatus.NotFound, ErrorCodes.AgentNotFound, "The agent does not exist or has been deregistered.");

    var now = timeProvider.GetUtcNow();

    // Soft delete: sets deregistered_at and keeps the row so tasks and audit events stay referentially intact.
    unitOfWork.AgentRepository.Delete(agent);

    var credentials = await unitOfWork.AgentCredentialRepository.ListByAgentIdAsync(agentId, trackChanges: true, cancellationToken);
    foreach (var credential in credentials)
      credential.Revoke(now);

    await unitOfWork.AuditEventRepository.AddAsync(AuditEvent.Create(AuditEventTypes.AgentDeregistered, now, agentId), cancellationToken);
    await unitOfWork.SaveChangesAsync(cancellationToken);

    // Same reasoning as in RegisterAsync: the database change is already committed.
    try
    {
      await presenceStore.RemoveAsync(agentId, cancellationToken);
    }
    catch (StateStoreUnavailableException ex)
    {
      logger.LogWarning(ex, "Could not remove presence keys for agent {AgentId} during deregistration.", agentId);
    }

    return Result.Success();
  }

  public async Task<Result<AgentDetailResponse>> GetAsync(Guid agentId, AuthenticatedAgentContext caller, CancellationToken cancellationToken = default)
  {
    // Evaluation order is 404 before 403 (decision A2).
    var agent = await unitOfWork.AgentRepository.GetWithCapabilitiesAsync(agentId, cancellationToken);
    if (agent is null)
      return Result<AgentDetailResponse>.Failure(ResultStatus.NotFound, ErrorCodes.AgentNotFound, "The agent does not exist or has been deregistered.");

    // Access rule (spec section 1.8): the agent itself, the same domain, or a domain pair on the allowlist.
    var allowed = caller.AgentId == agent.Id
      || string.Equals(caller.Domain, agent.Domain, StringComparison.OrdinalIgnoreCase)
      || policyOptions.Value.DomainPolicy.CrossDomainRules.Any(rule =>
          string.Equals(rule.CallerDomain, caller.Domain, StringComparison.OrdinalIgnoreCase)
          && string.Equals(rule.TargetDomain, agent.Domain, StringComparison.OrdinalIgnoreCase));

    if (!allowed)
      return Result<AgentDetailResponse>.Failure(ResultStatus.Forbidden, ErrorCodes.DomainForbidden, "Cross-domain access is not allowed by policy.");

    var presence = await presenceStore.GetAsync(agentId, cancellationToken);
    return Result<AgentDetailResponse>.Success(agent.ToDetailResponse(presence));
  }
}

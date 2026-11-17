using System.Text.Json;
using Application.DTOs.Result.Responses;
using Domain.Entities;

namespace Application.Interfaces.Policies;

/// <summary>
/// Pure policy stages needed in Week 6 (contract section 1.5, owner: task B-01). Preconditions: caller and target are
/// loaded and active, the target with its active capabilities. Order: domain scope, capability exists, input valid.
/// Returns the first failing stage: DOMAIN_FORBIDDEN (403), CAPABILITY_NOT_FOUND (404) or
/// VALIDATION_ERROR (400, details.errors paths under /payload/input).
/// </summary>
public interface ITaskPolicyEvaluator
{
  Result Evaluate(TaskPolicyRequest request);

  /// <summary>
  /// Recursion protection (A54, A55). Pure, no I/O. DEPTH_EXCEEDED when depth is greater than MaxRecursionDepth, otherwise
  /// CYCLE_DETECTED when targetAgentId is in ancestorChain (agent granularity). Depth is checked first. The failures carry no
  /// structured details: TaskService attaches the A57 details because it knows the root task id.
  /// </summary>
  Result EvaluateRecursion(Guid targetAgentId, int depth, IReadOnlyList<Guid> ancestorChain);
}

public sealed record TaskPolicyRequest(Agent Caller, Agent Target, string Capability, JsonElement Input);

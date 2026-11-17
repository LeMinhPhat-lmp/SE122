using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.Interfaces.Policies;
using Application.Interfaces.Validation;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.Policies;

/// <summary>
/// Domain scope, capability exists, input valid. Pure: it reads only the loaded entities and the request, never a
/// repository or Redis. Rate limiting is done by IRateLimiter; depth and cycle are in EvaluateRecursion.
/// </summary>
public sealed class TaskPolicyEvaluator(
  IDomainAccessPolicy domainAccessPolicy,
  IInputSchemaValidator inputSchemaValidator,
  IOptions<AersPolicyOptions> policyOptions) : ITaskPolicyEvaluator
{
  public Result Evaluate(TaskPolicyRequest request)
  {
    if (!domainAccessPolicy.CanAccess(request.Caller.Domain, request.Target.Domain))
      return Result.Failure(ResultStatus.Forbidden, ErrorCodes.DomainForbidden, "Cross-domain access is not allowed by policy.");

    var capability = request.Target.Capabilities.FirstOrDefault(candidate =>
      !candidate.IsDeleted && string.Equals(candidate.Name, request.Capability, StringComparison.Ordinal));
    if (capability is null)
      return Result.Failure(ResultStatus.NotFound, ErrorCodes.CapabilityNotFound, "The target agent has no such capability.");

    var violations = inputSchemaValidator.Validate(capability.InputSchemaJson, request.Input);
    if (violations.Count > 0)
    {
      var details = violations.Select(violation => new ErrorDetail("/payload/input" + violation.Path, violation.Message)).ToList();
      return Result.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.", details);
    }

    return Result.Success();
  }

  // Depth first, then cycle (A53 to A55). The boundary is depth > MaxRecursionDepth, so depth 0..Max is allowed.
  public Result EvaluateRecursion(Guid targetAgentId, int depth, IReadOnlyList<Guid> ancestorChain)
  {
    if (depth > policyOptions.Value.MaxRecursionDepth)
      return Result.Failure(ResultStatus.Conflict, ErrorCodes.DepthExceeded, "The maximum recursion depth was exceeded.");

    if (ancestorChain.Contains(targetAgentId))
      return Result.Failure(ResultStatus.Conflict, ErrorCodes.CycleDetected, "The target agent is already part of the call chain.");

    return Result.Success();
  }
}

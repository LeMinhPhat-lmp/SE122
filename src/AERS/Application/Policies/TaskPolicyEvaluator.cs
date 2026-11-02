using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.Interfaces.Policies;
using Application.Interfaces.Validation;

namespace Application.Policies;

/// <summary>
/// Domain scope, capability exists, input valid. Pure: it reads only the loaded entities and the request, never a
/// repository or Redis. Rate limit, depth and cycle checks are not part of Week 6 (Week 8).
/// </summary>
public sealed class TaskPolicyEvaluator(IDomainAccessPolicy domainAccessPolicy, IInputSchemaValidator inputSchemaValidator) : ITaskPolicyEvaluator
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
}

using Application.DTOs.Agent.Requests;
using Application.Options;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Application.Validators;

// Rules: Protocol-Specification-v3.md section 1.2.1. Limits come from Aers:RegistrationLimits.
// The service trims the name and lowercases the domain before this validator runs.
public sealed class AgentCardSubmissionValidator : AbstractValidator<AgentCardSubmission>
{
  private const string DomainPattern = @"^[a-z0-9][a-z0-9-]{0,63}\z";
  private const int OwnerRefMaxLength = 256;

  public AgentCardSubmissionValidator(
    IValidator<CapabilitySubmission> capabilityValidator,
    IOptions<RegistrationLimitsOptions> limitsOptions)
  {
    var limits = limitsOptions.Value;

    RuleFor(entity => entity.Name)
      .Cascade(CascadeMode.Stop)
      .NotEmpty().WithMessage("'name' must not be empty.")
      .MaximumLength(limits.MaxNameLength).WithMessage($"'name' must be at most {limits.MaxNameLength} characters.");

    RuleFor(entity => entity.Domain)
      .Cascade(CascadeMode.Stop)
      .NotEmpty().WithMessage("'domain' must not be empty.")
      .Matches(DomainPattern).WithMessage("'domain' must match ^[a-z0-9][a-z0-9-]{0,63}$ after normalization.");

    RuleFor(entity => entity.OwnerRef)
      .MaximumLength(OwnerRefMaxLength).WithMessage($"'ownerRef' must be at most {OwnerRefMaxLength} characters.")
      .When(entity => entity.OwnerRef is not null);

    RuleFor(entity => entity.Capabilities)
      .Cascade(CascadeMode.Stop)
      .Must(capabilities => capabilities.Count >= 1 && capabilities.Count <= limits.MaxCapabilities)
        .WithMessage($"'capabilities' must contain between 1 and {limits.MaxCapabilities} items.")
      .Must(capabilities => capabilities.Select(capability => capability.Name).Distinct(StringComparer.Ordinal).Count() == capabilities.Count)
        .WithMessage("Capability names must be unique within an agent (case-sensitive).");

    RuleForEach(entity => entity.Capabilities)
      .SetValidator(capabilityValidator);
  }
}

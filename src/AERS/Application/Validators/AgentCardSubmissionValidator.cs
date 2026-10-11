using Application.DTOs.Agent.Requests;
using FluentValidation;

namespace Application.Validators;

public sealed class AgentCardSubmissionValidator : AbstractValidator<AgentCardSubmission>
{
  public AgentCardSubmissionValidator(IValidator<CapabilitySubmission> capabilityValidator)
  {
    RuleFor(entity => entity.Name)
      .NotEmpty()
      .MaximumLength(50);

    RuleFor(entity => entity.Description)
      .NotEmpty()
      .MaximumLength(200);

    RuleFor(entity => entity.Domain)
      .NotEmpty()
      .MaximumLength(100);

    RuleForEach(entity => entity.Capabilities)
      .NotNull()
      .SetValidator(capabilityValidator);
  }
}
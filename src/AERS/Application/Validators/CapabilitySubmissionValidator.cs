using Application.DTOs.Agent.Requests;
using Application.DTOs.Agent.Responses;
using FluentValidation;

namespace Application.Validators;

public sealed class CapabilitySubmissionValidator : AbstractValidator<CapabilitySubmission>
{
  public CapabilitySubmissionValidator()
  {
    RuleFor(entity => entity.Name)
      .NotEmpty()
      .MaximumLength(50);

    RuleFor(entity => entity.Description)
      .NotEmpty()
      .MaximumLength(200);

    RuleFor(entity => entity.InputSchema)
      .NotNull();

    RuleFor(entity => entity.OutputSchema)
      .NotNull();
  }
}
using Application.DTOs.Agent.Requests;
using FluentValidation;

namespace Application.Validators;

public sealed class AgentRegistrationRequestValidator : AbstractValidator<AgentRegistrationRequest>
{
  public AgentRegistrationRequestValidator(IValidator<AgentCardSubmission> agentCardSubmissionValidator)
  {
    RuleFor(entity => entity.AgentCardSubmission)
      .NotNull()
      .SetValidator(agentCardSubmissionValidator);
  }
}
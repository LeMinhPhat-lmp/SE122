using System.Text;
using Application.DTOs.Agent.Requests;
using Application.Interfaces.Validation;
using Application.Options;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Application.Validators;

public sealed class CapabilitySubmissionValidator : AbstractValidator<CapabilitySubmission>
{
  private const string NamePattern = @"^[A-Za-z][A-Za-z0-9_.-]{0,99}\z";

  public CapabilitySubmissionValidator(
    IOptions<RegistrationLimitsOptions> limitsOptions,
    ISchemaMetaValidator schemaValidator)
  {
    var maxSchemaBytes = limitsOptions.Value.MaxSchemaBytes;

    RuleFor(entity => entity.Name)
      .Cascade(CascadeMode.Stop)
      .NotEmpty().WithMessage("'name' must not be empty.")
      .Matches(NamePattern).WithMessage("'name' must match ^[A-Za-z][A-Za-z0-9_.-]{0,99}$.");

    RuleFor(entity => entity.InputSchema)
      .Cascade(CascadeMode.Stop)
      .Must(schema => Encoding.UTF8.GetByteCount(schema.GetRawText()) <= maxSchemaBytes)
        .WithMessage($"'inputSchema' must be at most {maxSchemaBytes} bytes.")
      .Must(schemaValidator.IsValidDraft202012Schema)
        .WithMessage("'inputSchema' is not a valid JSON Schema Draft 2020-12 document.");

    RuleFor(entity => entity.OutputSchema)
      .Cascade(CascadeMode.Stop)
      .Must(schema => Encoding.UTF8.GetByteCount(schema.GetRawText()) <= maxSchemaBytes)
        .WithMessage($"'outputSchema' must be at most {maxSchemaBytes} bytes.")
      .Must(schemaValidator.IsValidDraft202012Schema)
        .WithMessage("'outputSchema' is not a valid JSON Schema Draft 2020-12 document.");
  }
}

using System.Text.Json;
using Application.DTOs.Agent.Requests;
using Application.Interfaces.Validation;
using Application.Options;
using Application.Validators;
using FluentValidation;
using Infrastructure.Validation;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class ReferenceAgentContractTests
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public void JobHuntCardsAndExamplesSatisfyTheRegistrationContract()
  {
    var root = FindSolutionRoot();
    var directory = Path.Combine(root, "agents", "jobhunt");
    var cards = Directory.GetFiles(directory, "*.agent-card.json").OrderBy(path => path).ToArray();
    Assert.Equal(4, cards.Length);

    var metaValidator = new JsonSchemaMetaValidator();
    var inputValidator = new JsonSchemaInputValidator();
    var registrationValidator = CreateRegistrationValidator(metaValidator);
    var names = new HashSet<string>(StringComparer.Ordinal);

    foreach (var cardPath in cards)
    {
      using var document = JsonDocument.Parse(File.ReadAllText(cardPath));
      var rootElement = document.RootElement;
      Assert.False(ContainsProperty(rootElement, "agentId"), cardPath);

      var request = JsonSerializer.Deserialize<AgentRegistrationRequest>(rootElement.GetRawText(), JsonOptions);
      Assert.NotNull(request);
      Assert.Equal("jobhunt", request.AgentCard.Domain);
      Assert.Empty(registrationValidator.Validate(request).Errors);

      foreach (var capability in request.AgentCard.Capabilities)
      {
        Assert.True(names.Add(capability.Name), $"Duplicate capability: {capability.Name}");
        Assert.True(metaValidator.IsValidDraft202012Schema(capability.InputSchema), capability.Name);
        Assert.True(metaValidator.IsValidDraft202012Schema(capability.OutputSchema), capability.Name);

        var examples = Path.Combine(directory, "examples");
        var valid = Directory.GetFiles(examples, $"{capability.Name}.valid.json");
        var invalid = Directory.GetFiles(examples, $"{capability.Name}.invalid*.json");
        Assert.Single(valid);
        Assert.True(invalid.Length >= 2, capability.Name);

        foreach (var path in valid)
        {
          using var input = JsonDocument.Parse(File.ReadAllText(path));
          Assert.Empty(inputValidator.Validate(capability.InputSchema, input.RootElement));
        }

        foreach (var path in invalid)
        {
          using var input = JsonDocument.Parse(File.ReadAllText(path));
          Assert.NotEmpty(inputValidator.Validate(capability.InputSchema, input.RootElement));
        }

        var outputPath = Path.Combine(examples, $"{capability.Name}.output.json");
        using var output = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.Empty(inputValidator.Validate(capability.OutputSchema, output.RootElement));
      }
    }

    Assert.Equal(4, names.Count);
  }

  [Fact]
  public void BrokenCardShapesAreRejectedByContractChecks()
  {
    using var inputSchema = JsonDocument.Parse("""
      {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "type": "object",
        "properties": { "name": { "type": "string" } },
        "required": ["name"],
        "additionalProperties": false
      }
      """);
    var validator = new JsonSchemaInputValidator();

    using var extraProperty = JsonDocument.Parse("""{"name":"ok","extra":true}""");
    using var missingRequired = JsonDocument.Parse("""{}""");
    Assert.NotEmpty(validator.Validate(inputSchema.RootElement, extraProperty.RootElement));
    Assert.NotEmpty(validator.Validate(inputSchema.RootElement, missingRequired.RootElement));

    using var cardWithIdentity = JsonDocument.Parse("""{"agentCard":{"agentId":"must-not-be-present"}}""");
    Assert.True(ContainsProperty(cardWithIdentity.RootElement, "agentId"));
  }

  private static IValidator<AgentRegistrationRequest> CreateRegistrationValidator(ISchemaMetaValidator metaValidator)
  {
    var limits = Options.Create(new RegistrationLimitsOptions());
    var capability = new CapabilitySubmissionValidator(limits, metaValidator);
    var card = new AgentCardSubmissionValidator(capability, limits);
    return new AgentRegistrationRequestValidator(card);
  }

  private static string FindSolutionRoot()
  {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (Directory.Exists(Path.Combine(directory.FullName, "agents", "jobhunt")))
        return directory.FullName;
      directory = directory.Parent;
    }

    throw new DirectoryNotFoundException(
      $"Could not locate the solution root containing agents/jobhunt from '{AppContext.BaseDirectory}'.");
  }

  private static bool ContainsProperty(JsonElement element, string propertyName)
  {
    if (element.ValueKind == JsonValueKind.Object)
      foreach (var property in element.EnumerateObject())
        if (property.NameEquals(propertyName) || ContainsProperty(property.Value, propertyName))
          return true;

    if (element.ValueKind == JsonValueKind.Array)
      foreach (var item in element.EnumerateArray())
        if (ContainsProperty(item, propertyName))
          return true;

    return false;
  }
}

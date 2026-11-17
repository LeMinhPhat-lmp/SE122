using System.Text.Json;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.Interfaces.Policies;
using Application.Interfaces.Validation;
using Application.Policies;
using Application.DTOs.Result.Responses;
using Domain.Entities;
using Infrastructure.Validation;
using Xunit;

namespace AERS.UnitTests;

public sealed class TaskPolicyEvaluatorTests
{
  [Fact]
  public void SameDomain_IsAllowed()
  {
    var evaluator = CreateEvaluator((_, _) => true, new StubInputSchemaValidator());

    var result = evaluator.Evaluate(Request("hr", "hr", "analyze"));

    Assert.True(result.IsSuccess);
  }

  [Fact]
  public void CrossDomain_IsDeniedByDefault()
  {
    var evaluator = CreateEvaluator((_, _) => false, new StubInputSchemaValidator());

    var result = evaluator.Evaluate(Request("hr", "jobhunt", "analyze"));

    Assert.Equal(ResultStatus.Forbidden, result.ResultStatus);
    Assert.Equal(ErrorCodes.DomainForbidden, result.ErrorCode);
  }

  [Fact]
  public void DirectedCrossDomainRule_IsAllowed_AndReverseDirectionIsDenied()
  {
    var evaluator = CreateEvaluator((caller, target) => caller == "hr" && target == "jobhunt", new StubInputSchemaValidator());

    var allowed = evaluator.Evaluate(Request("hr", "jobhunt", "analyze"));
    var denied = evaluator.Evaluate(Request("jobhunt", "hr", "analyze"));

    Assert.True(allowed.IsSuccess);
    Assert.Equal(ErrorCodes.DomainForbidden, denied.ErrorCode);
  }

  [Fact]
  public void CapabilityMatching_IsOrdinalAndCaseSensitive()
  {
    var evaluator = CreateEvaluator((_, _) => true, new StubInputSchemaValidator());

    var exact = evaluator.Evaluate(Request("hr", "hr", "analyze"));
    var differentCase = evaluator.Evaluate(Request("hr", "hr", "Analyze", "analyze"));

    Assert.True(exact.IsSuccess);
    Assert.Equal(ErrorCodes.CapabilityNotFound, differentCase.ErrorCode);
  }

  [Fact]
  public void SoftDeletedCapability_IsNotFound()
  {
    var target = CreateAgent("hr", "analyze");
    target.Capabilities.Single().DeletedAt = DateTimeOffset.UtcNow;
    var evaluator = new TaskPolicyEvaluator(new FakeDomainAccessPolicy((_, _) => true), new StubInputSchemaValidator());

    var result = evaluator.Evaluate(new TaskPolicyRequest(CreateAgent("hr"), target, "analyze", EmptyObject()));

    Assert.Equal(ErrorCodes.CapabilityNotFound, result.ErrorCode);
  }

  [Fact]
  public void StageOrder_DomainFailureWinsOverCapability()
  {
    var evaluator = CreateEvaluator((_, _) => false, new StubInputSchemaValidator { Errors = [new("/field", "invalid")] });

    var result = evaluator.Evaluate(Request("hr", "jobhunt", "missing"));

    Assert.Equal(ErrorCodes.DomainForbidden, result.ErrorCode);
  }

  [Fact]
  public void StageOrder_CapabilityFailureWinsOverInput()
  {
    var evaluator = CreateEvaluator((_, _) => true, new StubInputSchemaValidator { Errors = [new("/field", "invalid")] });

    var result = evaluator.Evaluate(Request("hr", "hr", "missing"));

    Assert.Equal(ErrorCodes.CapabilityNotFound, result.ErrorCode);
  }

  [Fact]
  public void InvalidInput_UsesContractMessageAndPrefixedPaths()
  {
    var evaluator = CreateEvaluator((_, _) => true,
      new StubInputSchemaValidator { Errors = [new("/cvText", "A value is required."), new("", "The input is invalid.")] });

    var result = evaluator.Evaluate(Request("hr", "hr", "analyze"));

    Assert.Equal(ResultStatus.ValidationError, result.ResultStatus);
    Assert.Equal(ErrorCodes.ValidationError, result.ErrorCode);
    Assert.Equal("The request is invalid.", Assert.Single(result.Errors));
    Assert.Equal(["/payload/input/cvText", "/payload/input"], result.ErrorDetails.Select(detail => detail.Path));
  }

  [Fact]
  public void Validator_AcceptsValidInputAndEmptySchema()
  {
    var validator = new JsonSchemaInputValidator();

    Assert.Empty(validator.Validate(Schema("""{"type":"object","required":["cvText"],"properties":{"cvText":{"type":"string"}}}"""),
      Instance("""{"cvText":"text"}""")));
    Assert.Empty(validator.Validate(Schema("{}"), Instance("""{"anything":42}""")));
  }

  [Fact]
  public void ValidatorReportsMissingWrongTypeExtraAndNestedErrors()
  {
    var validator = new JsonSchemaInputValidator();
    var schema = Schema("""
      {
        "type":"object",
        "required":["cvText","profile"],
        "additionalProperties":false,
        "properties":{
          "cvText":{"type":"string"},
          "profile":{
            "type":"object",
            "required":["name"],
            "properties":{"name":{"type":"string"}}
          }
        }
      }
      """);

    var errors = validator.Validate(schema, Instance("""{"cvText":7,"profile":{"name":7},"extra":true}"""));

    Assert.Contains(errors, error => error.Path == "/cvText");
    Assert.Contains(errors, error => error.Path == "/profile/name");
    Assert.Contains(errors, error => error.Path == "/extra");
    Assert.True(errors.Count >= 3);
    Assert.All(errors, error => Assert.False(string.IsNullOrWhiteSpace(error.Message)));
  }

  [Fact]
  public void ValidatorSupportsRefsAndDoesNotAssertFormat()
  {
    var validator = new JsonSchemaInputValidator();
    var schema = Schema("""
      {
        "$defs":{"name":{"type":"string","minLength":2}},
        "type":"object",
        "properties":{"name":{"$ref":"#/$defs/name"},"email":{"type":"string","format":"email"}}
      }
      """);

    Assert.Empty(validator.Validate(schema, Instance("""{"name":"Ada","email":"not-an-email"}""")));
    Assert.Contains(validator.Validate(schema, Instance("""{"name":"A"}""")), error => error.Path == "/name");
  }

  [Fact]
  public void ValidatorSupportsDraft202012PrefixItems()
  {
    var validator = new JsonSchemaInputValidator();
    var schema = Schema("""{"type":"array","prefixItems":[{"type":"string"},{"type":"integer"}]}""");

    Assert.Empty(validator.Validate(schema, Instance("""["name", 1, true]""")));
    Assert.Contains(validator.Validate(schema, Instance("""[1, "wrong"]""")), error => error.Path == "/0" || error.Path == "/1");
  }

  [Fact]
  public void ValidatorThrowsForSchemaThatCannotBeBuilt()
  {
    var validator = new JsonSchemaInputValidator();

    Assert.Throws<InvalidOperationException>(() => validator.Validate(Schema("""{"type":42}"""), Instance("{}")));
  }

  private static TaskPolicyEvaluator CreateEvaluator(
    Func<string, string, bool> domainRule,
    StubInputSchemaValidator validator) =>
    new(new FakeDomainAccessPolicy(domainRule), validator);

  private static TaskPolicyRequest Request(string callerDomain, string targetDomain, string capability, string? targetCapability = null) =>
    new(CreateAgent(callerDomain), CreateAgent(targetDomain, targetCapability ?? (capability == "missing" ? "other" : capability)), capability, EmptyObject());

  private static Agent CreateAgent(string domain, params string[] capabilities)
  {
    var agent = Agent.Register(Guid.NewGuid(), "agent", domain, "description", null);
    foreach (var capability in capabilities)
      agent.AddCapability(capability, "description", EmptyObject(), EmptyObject());
    return agent;
  }

  private static JsonElement EmptyObject() => Instance("{}");

  private static JsonElement Schema(string json) => Instance(json);

  private static JsonElement Instance(string json) => JsonDocument.Parse(json).RootElement.Clone();

  private sealed class StubInputSchemaValidator : IInputSchemaValidator
  {
    public IReadOnlyList<ErrorDetail> Errors { get; init; } = [];

    public IReadOnlyList<ErrorDetail> Validate(JsonElement schema, JsonElement instance) => Errors;
  }
}

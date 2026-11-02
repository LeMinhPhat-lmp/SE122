using System.Text.Json;
using System.Text.Json.Nodes;
using Application.DTOs.Result.Responses;
using Application.Interfaces.Validation;
using Json.Schema;

namespace Infrastructure.Validation;

/// <summary>
/// Validates a task input against a capability input schema with JsonSchema.Net (Draft 2020-12, decision D-02).
/// Nothing is cached in Week 6. The 'format' keyword is an annotation only (not asserted), which is the library default.
/// </summary>
public sealed class JsonSchemaInputValidator : IInputSchemaValidator
{
  private static readonly EvaluationOptions ListOutput = new() { OutputFormat = OutputFormat.List };

  public IReadOnlyList<ErrorDetail> Validate(JsonElement schema, JsonElement instance)
  {
    JsonSchema jsonSchema;
    try
    {
      jsonSchema = JsonSchema.FromText(schema.GetRawText());
    }
    catch (Exception ex)
    {
      // The schema was meta-validated at registration; failing to build it now is a server problem, not bad input.
      throw new InvalidOperationException("The stored capability input schema cannot be built.", ex);
    }

    var results = jsonSchema.Evaluate(JsonNode.Parse(instance.GetRawText()), ListOutput);
    if (results.IsValid)
      return [];

    var violations = new List<ErrorDetail>();
    var seen = new HashSet<(string Path, string Message)>();

    foreach (var node in new[] { results }.Concat(results.Details))
    {
      if (node.Errors is null)
        continue;

      var path = node.InstanceLocation.ToString();
      foreach (var message in node.Errors.Values)
        if (seen.Add((path, message)))
          violations.Add(new ErrorDetail(path, message));
    }

    // Defensive: an invalid result without any message still has to be reported as invalid.
    if (violations.Count == 0)
      violations.Add(new ErrorDetail(string.Empty, "The input does not match the schema."));

    return violations;
  }
}

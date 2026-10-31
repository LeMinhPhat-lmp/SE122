using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace AERS.DummyAgent;

public static class SchemaValidation
{
  public static (bool IsValid, IReadOnlyList<string> Errors) Validate(JsonElement schema, string input)
  {
    JsonNode? instance;
    try
    {
      instance = JsonNode.Parse(input);
    }
    catch (JsonException ex)
    {
      return (false, [ex.Message]);
    }

    if (instance is null)
      return (false, ["The input JSON is empty."]);

    var result = JsonSchema.FromText(schema.GetRawText()).Evaluate(instance);
    if (result.IsValid)
      return (true, []);

    return (false, ["The input does not satisfy the discovered inputSchema."]);
  }
}

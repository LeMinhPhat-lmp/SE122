using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Interfaces.Validation;
using Json.Schema;

namespace Infrastructure.Validation;

/// <summary>Validates schemas against the Draft 2020-12 metaschema using JsonSchema.Net (decision D-02).</summary>
public sealed class JsonSchemaMetaValidator : ISchemaMetaValidator
{
  public bool IsValidDraft202012Schema(JsonElement schema)
  {
    if (schema.ValueKind != JsonValueKind.Object)
      return false;

    var node = JsonNode.Parse(schema.GetRawText());
    return MetaSchemas.Draft202012.Evaluate(node).IsValid;
  }
}

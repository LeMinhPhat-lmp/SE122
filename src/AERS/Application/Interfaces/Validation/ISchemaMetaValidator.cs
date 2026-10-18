using System.Text.Json;

namespace Application.Interfaces.Validation;

public interface ISchemaMetaValidator
{
  /// <summary>Checks that the element is a valid JSON Schema Draft 2020-12 document.</summary>
  bool IsValidDraft202012Schema(JsonElement schema);
}

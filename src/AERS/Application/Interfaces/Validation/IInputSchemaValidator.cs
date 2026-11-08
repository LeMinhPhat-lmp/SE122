using System.Text.Json;
using Application.DTOs.Result.Responses;

namespace Application.Interfaces.Validation;

public interface IInputSchemaValidator
{
  /// <summary>
  /// Validates an instance against a JSON Schema Draft 2020-12 schema. An empty list means valid.
  /// Paths are JSON Pointers relative to the instance (for example "/cvText"; empty string for the root).
  /// </summary>
  IReadOnlyList<ErrorDetail> Validate(JsonElement schema, JsonElement instance);
}

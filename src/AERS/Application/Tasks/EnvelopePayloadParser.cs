using System.Text.Json;
using Application.Constants;
using Application.DTOs.Result.Enums;
using Application.DTOs.Result.Responses;
using Application.DTOs.Tasks;

namespace Application.Tasks;

/// <summary>
/// Strict payload parsing (decision A37): unknown properties, wrong types and missing required properties are all
/// reported together as VALIDATION_ERROR with exact JSON Pointer paths under /payload. Property names are matched
/// exactly (camelCase). Hand-written instead of a serializer round trip so that every violation is collected and no
/// internal .NET type name can leak into a message.
/// </summary>
public static class EnvelopePayloadParser
{
  private const string Root = "/payload";

  public static Result<CreateTaskRequest> ParseCreate(JsonElement payload)
  {
    var errors = new List<ErrorDetail>();
    RejectUnknownProperties(payload, Root, ["targetAgentId", "capability", "input", "parentTaskId"], errors);

    var targetAgentId = ReadString(payload, Root, "targetAgentId", required: true, errors);
    var capability = ReadString(payload, Root, "capability", required: true, errors);
    var parentTaskId = ReadString(payload, Root, "parentTaskId", required: false, errors);

    JsonElement input = default;
    if (!payload.TryGetProperty("input", out input))
      errors.Add(new ErrorDetail(Root + "/input", "input is required."));

    return errors.Count > 0
      ? Invalid<CreateTaskRequest>(errors)
      : Result<CreateTaskRequest>.Success(new CreateTaskRequest(targetAgentId!, capability!, input, parentTaskId));
  }

  public static Result<TaskResult> ParseResult(JsonElement payload)
  {
    var errors = new List<ErrorDetail>();
    RejectUnknownProperties(payload, Root, ["taskId", "status", "output", "error"], errors);

    var taskId = ReadString(payload, Root, "taskId", required: true, errors);
    var status = ReadString(payload, Root, "status", required: true, errors);

    JsonElement? output = null;
    if (payload.TryGetProperty("output", out var outputValue) && outputValue.ValueKind != JsonValueKind.Null)
      output = outputValue;

    ErrorPayload? error = null;
    if (payload.TryGetProperty("error", out var errorValue) && errorValue.ValueKind != JsonValueKind.Null)
    {
      if (errorValue.ValueKind != JsonValueKind.Object)
        errors.Add(new ErrorDetail(Root + "/error", "error must be an object or null."));
      else
        error = ReadError(errorValue, errors);
    }

    return errors.Count > 0
      ? Invalid<TaskResult>(errors)
      : Result<TaskResult>.Success(new TaskResult(taskId!, status!, output, error));
  }

  public static Result<TaskStatusEvent> ParseStatusEvent(JsonElement payload)
  {
    var errors = new List<ErrorDetail>();
    RejectUnknownProperties(payload, Root, ["taskId", "status", "result"], errors);

    var taskId = ReadString(payload, Root, "taskId", required: true, errors);
    var status = ReadString(payload, Root, "status", required: true, errors);

    if (payload.TryGetProperty("result", out var resultValue) && resultValue.ValueKind != JsonValueKind.Null)
      errors.Add(new ErrorDetail(Root + "/result", "result must be null."));

    return errors.Count > 0
      ? Invalid<TaskStatusEvent>(errors)
      : Result<TaskStatusEvent>.Success(new TaskStatusEvent(taskId!, status!));
  }

  // code and message are read as optional here; ITaskService rejects empty values for a failed result with exact paths.
  private static ErrorPayload ReadError(JsonElement error, List<ErrorDetail> errors)
  {
    const string path = Root + "/error";
    RejectUnknownProperties(error, path, ["code", "message", "details", "retryable"], errors);

    var code = ReadString(error, path, "code", required: false, errors);
    var message = ReadString(error, path, "message", required: false, errors);

    JsonElement? details = null;
    if (error.TryGetProperty("details", out var detailsValue) && detailsValue.ValueKind != JsonValueKind.Null)
      details = detailsValue;

    bool? retryable = null;
    if (error.TryGetProperty("retryable", out var retryableValue) && retryableValue.ValueKind != JsonValueKind.Null)
    {
      if (retryableValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        retryable = retryableValue.GetBoolean();
      else
        errors.Add(new ErrorDetail(path + "/retryable", "retryable must be a boolean or null."));
    }

    return new ErrorPayload(code ?? string.Empty, message ?? string.Empty, details, retryable);
  }

  private static void RejectUnknownProperties(JsonElement element, string path, string[] allowed, List<ErrorDetail> errors)
  {
    foreach (var property in element.EnumerateObject())
      if (!Array.Exists(allowed, name => string.Equals(name, property.Name, StringComparison.Ordinal)))
        errors.Add(new ErrorDetail($"{path}/{EscapePointer(property.Name)}", "Unknown property."));
  }

  private static string? ReadString(JsonElement element, string path, string name, bool required, List<ErrorDetail> errors)
  {
    if (!element.TryGetProperty(name, out var value))
    {
      if (required)
        errors.Add(new ErrorDetail($"{path}/{name}", $"{name} is required."));
      return null;
    }

    if (value.ValueKind == JsonValueKind.Null)
    {
      if (required)
        errors.Add(new ErrorDetail($"{path}/{name}", $"{name} must not be null."));
      return null;
    }

    if (value.ValueKind != JsonValueKind.String)
    {
      errors.Add(new ErrorDetail($"{path}/{name}", $"{name} must be a string."));
      return null;
    }

    return value.GetString();
  }

  // RFC 6901: "~" becomes "~0" and "/" becomes "~1".
  private static string EscapePointer(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

  private static Result<T> Invalid<T>(List<ErrorDetail> errors) =>
    Result<T>.Failure(ResultStatus.ValidationError, ErrorCodes.ValidationError, "The request is invalid.", errors);
}

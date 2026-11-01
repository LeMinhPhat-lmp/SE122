using System.Text.Json;

namespace AERS.AgentClient;

public sealed class AersApiException(
  int statusCode,
  string code,
  string message,
  JsonElement? details = null) : Exception(message)
{
  public int StatusCode { get; } = statusCode;
  public string Code { get; } = code;
  public JsonElement? Details { get; } = details;
}

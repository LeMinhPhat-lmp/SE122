using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Tasks;

/// <summary>
/// The one JsonSerializerOptions instance used to put DTOs into an envelope payload. camelCase, and null members are
/// written explicitly because the contract says optional values are present with null (decisions A24, A29).
/// Parsing of incoming payloads is done by <see cref="EnvelopePayloadParser"/>, not by this instance.
/// </summary>
public static class EnvelopeJson
{
  public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
  };
}

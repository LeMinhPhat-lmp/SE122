using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AERS.AgentClient;

public sealed class AersClient
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.Never
  };

  private readonly HttpClient http;
  private readonly AersClientOptions options;

  public AersClient(HttpClient http, AersClientOptions options)
  {
    ArgumentNullException.ThrowIfNull(http);
    ArgumentNullException.ThrowIfNull(options);
    if (!options.BaseUrl.IsAbsoluteUri)
      throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(options));

    this.http = http;
    this.options = options;
  }

  public async Task<RegistrationResult> RegisterAsync(
    AgentCardSubmission card, CancellationToken ct = default)
  {
    using var request = CreateRequest(HttpMethod.Post, "agents/register");
    AddHeader(request, "X-Registration-Key", options.RegistrationKey);
    request.Content = JsonContent.Create(card, options: JsonOptions);

    var result = await SendAsync<RegistrationResult>(request, ct);
    options.ApiKey = result.ApiKey;
    return result;
  }

  public Task HeartbeatAsync(Guid agentId, CancellationToken ct = default) =>
    SendNoContentAsync(HttpMethod.Post, $"agents/{agentId:D}/heartbeat", ct);

  public Task DeregisterAsync(Guid agentId, CancellationToken ct = default) =>
    SendNoContentAsync(HttpMethod.Delete, $"agents/{agentId:D}", ct);

  public Task<AgentDetail> GetAgentAsync(Guid agentId, CancellationToken ct = default) =>
    SendAsync<AgentDetail>(CreateAuthenticatedRequest(HttpMethod.Get, $"agents/{agentId:D}"), ct);

  public Task<AgentPage> ListAgentsAsync(AgentQuery query, CancellationToken ct = default) =>
    ListAgentsPageAsync(query, null, ct);

  public async IAsyncEnumerable<AgentCard> ListAllAgentsAsync(
    AgentQuery query,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
  {
    await foreach (var agent in ListAllAgentsAsync(query, null, ct))
      yield return agent;
  }

  public async IAsyncEnumerable<AgentCard> ListAllAgentsAsync(
    AgentQuery query,
    Action<int, int>? pageObserver,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);
    var cursor = (string?)null;
    var pageNumber = 0;
    do
    {
      var page = await ListAgentsPageAsync(query, cursor, ct);
      pageObserver?.Invoke(++pageNumber, page.Agents.Count);
      foreach (var agent in page.Agents)
        yield return agent;
      cursor = page.NextCursor;
    }
    while (cursor is not null);
  }

  public Task<AgentCapabilities> GetCapabilitiesAsync(Guid agentId, CancellationToken ct = default) =>
    SendAsync<AgentCapabilities>(
      CreateAuthenticatedRequest(HttpMethod.Get, $"agents/{agentId:D}/capabilities"), ct);

  internal async Task SendHeartbeatAsync(Guid agentId, CancellationToken ct) =>
    await HeartbeatAsync(agentId, ct);

  private async Task<AgentPage> ListAgentsPageAsync(
    AgentQuery query, string? cursor, CancellationToken ct)
  {
    ArgumentNullException.ThrowIfNull(query);
    var uri = BuildQueryUri("agents", query, cursor);
    return await SendAsync<AgentPage>(CreateAuthenticatedRequest(HttpMethod.Get, uri), ct);
  }

  private async Task SendNoContentAsync(HttpMethod method, string uri, CancellationToken ct)
  {
    using var request = CreateAuthenticatedRequest(method, uri);
    await SendAsync(request, ct);
  }

  private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string uri)
  {
    var request = CreateRequest(method, uri);
    AddHeader(request, "X-Api-Key", options.ApiKey);
    return request;
  }

  private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
  {
    var request = new HttpRequestMessage(method, new Uri(options.BaseUrl, uri));
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    return request;
  }

  private static void AddHeader(HttpRequestMessage request, string name, string? value)
  {
    if (!string.IsNullOrWhiteSpace(value))
      request.Headers.TryAddWithoutValidation(name, value);
  }

  private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
  {
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (!response.IsSuccessStatusCode)
      throw await CreateApiExceptionAsync(response, ct);

    return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
      ?? throw new AersApiException((int)response.StatusCode, "UNKNOWN_ERROR", "The response body was empty.");
  }

  private async Task SendAsync(HttpRequestMessage request, CancellationToken ct)
  {
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    if (!response.IsSuccessStatusCode)
      throw await CreateApiExceptionAsync(response, ct);
  }

  private static async Task<AersApiException> CreateApiExceptionAsync(
    HttpResponseMessage response, CancellationToken ct)
  {
    var body = await response.Content.ReadAsStringAsync(ct);
    try
    {
      using var document = JsonDocument.Parse(body);
      var root = document.RootElement;
      var code = root.TryGetProperty("code", out var codeElement)
        ? codeElement.GetString()
        : null;
      var message = root.TryGetProperty("message", out var messageElement)
        ? messageElement.GetString()
        : null;
      var details = root.TryGetProperty("details", out var detailsElement)
        ? detailsElement.Clone()
        : (JsonElement?)null;
      return new AersApiException(
        (int)response.StatusCode,
        code ?? "UNKNOWN_ERROR",
        message ?? response.ReasonPhrase ?? "The request failed.",
        details);
    }
    catch (JsonException)
    {
      return new AersApiException(
        (int)response.StatusCode,
        "UNKNOWN_ERROR",
        response.ReasonPhrase ?? "The request failed.");
    }
  }

  private static string BuildQueryUri(string path, AgentQuery query, string? cursor)
  {
    var values = new List<string>();
    if (query.Domain is not null)
      values.Add($"domain={Uri.EscapeDataString(query.Domain)}");
    if (query.Capability is not null)
      values.Add($"capability={Uri.EscapeDataString(query.Capability)}");
    if (query.OnlineOnly)
      values.Add("onlineOnly=true");
    if (query.Limit is not null)
      values.Add($"limit={query.Limit.Value}");
    if (cursor is not null)
      values.Add($"cursor={Uri.EscapeDataString(cursor)}");

    return values.Count == 0 ? path : $"{path}?{string.Join("&", values)}";
  }
}

using System.Collections.Concurrent;
using System.Text.Json;
using AERS.AgentClient;
using AERS.DummyAgent.Core.Behaviors;
using Microsoft.Extensions.Logging;

namespace AERS.DummyAgent.Core;

public sealed record DummyAgentOptions(
  Uri BaseUrl,
  string? RegistrationKey,
  string Name,
  string Domain,
  IReadOnlyList<string> Capabilities)
{
  public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(15);
  public TimeSpan ChildCallTimeout { get; init; } = TimeSpan.FromSeconds(10);
  public bool Connect { get; init; } = true;
  public ILoggerFactory? LoggerFactory { get; init; }
}

public sealed record InvocationRecord(
  Guid TaskId,
  string Capability,
  Guid? ParentTaskId,
  int Depth);

public sealed class DummyAgent : IAsyncDisposable
{
  private readonly DummyAgentOptions options;
  private readonly ILogger<DummyAgent>? logger;
  private readonly ConcurrentQueue<InvocationRecord> invocations = new();
  private HttpClient? http;
  private AersClient? client;
  private AersAgentConnection? connection;
  private CancellationTokenSource? heartbeatCancellation;
  private Task? heartbeatTask;
  private int stopped;

  public DummyAgent(DummyAgentOptions options)
  {
    ArgumentNullException.ThrowIfNull(options);
    if (options.Capabilities.Any(name => !DummyCatalog.All.Any(definition => definition.Name == name)))
      throw new ArgumentException("All capabilities must exist in DummyCatalog.", nameof(options));
    this.options = options;
    logger = options.LoggerFactory?.CreateLogger<DummyAgent>();
  }

  public Guid AgentId { get; private set; }
  public string DisplayName => options.Name;
  public string MaskedApiKey { get; private set; } = "********";
  public AersClient Client => client ?? throw new InvalidOperationException("The agent has not started.");
  public AersAgentConnection Connection => connection ?? throw new InvalidOperationException("The agent has not started.");
  public IReadOnlyList<InvocationRecord> Invocations => invocations.ToArray();
  public event Action<InvocationRecord, InvocationResult>? InvocationCompleted;

  public async Task StartAsync(CancellationToken ct = default)
  {
    if (AgentId != Guid.Empty)
      return;

    http = new HttpClient();
    client = new AersClient(http, new AersClientOptions
    {
      BaseUrl = options.BaseUrl,
      RegistrationKey = options.RegistrationKey
    });
    var registration = await client.RegisterAsync(CreateCard(), ct);
    AgentId = registration.AgentId;
    MaskedApiKey = Mask(registration.ApiKey);

    if (options.Connect)
    {
      connection = new AersAgentConnection(new(
        new Uri(options.BaseUrl, "/hubs/agent"),
        registration.ApiKey));
      connection.SetInvocationHandler(HandleInvocationAsync);
      await connection.ConnectAsync(ct);
    }

    heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
    heartbeatTask = new AgentHeartbeatRunner(client).RunAsync(AgentId, options.HeartbeatInterval, heartbeatCancellation.Token);
  }

  public async Task StopAsync(CancellationToken ct = default)
  {
    if (Interlocked.Exchange(ref stopped, 1) != 0)
      return;

    heartbeatCancellation?.Cancel();
    if (heartbeatTask is not null)
    {
      try
      {
        await heartbeatTask;
      }
      catch (OperationCanceledException) when (heartbeatCancellation?.IsCancellationRequested == true)
      {
      }
    }

    if (connection is not null)
      await connection.DisposeAsync();
    if (client is not null && AgentId != Guid.Empty)
    {
      try
      {
        await client.DeregisterAsync(AgentId, ct);
      }
      catch (Exception ex)
      {
        logger?.LogWarning(ex, "Dummy agent deregistration failed.");
      }
    }

    heartbeatCancellation?.Dispose();
    http?.Dispose();
  }

  public override string ToString() =>
    $"{DisplayName} ({options.Domain}) id={AgentId}";

  public async ValueTask DisposeAsync() => await StopAsync();

  private AgentCardSubmission CreateCard() =>
    new(options.Name, options.Domain, "AERS deterministic dummy agent.", null,
      options.Capabilities.Select(name =>
      {
        var definition = DummyCatalog.Get(name);
        return new Capability(definition.Name, definition.Description, definition.InputSchema, definition.OutputSchema);
      }).ToArray());

  private async Task<InvocationResult> HandleInvocationAsync(
    TaskInvocation invocation,
    InvocationContext _,
    CancellationToken cancellationToken)
  {
    var record = new InvocationRecord(invocation.TaskId, invocation.Capability, invocation.ParentTaskId, invocation.Depth);
    invocations.Enqueue(record);
    if (!DummyCatalog.All.Any(definition => definition.Name == invocation.Capability))
    {
      var unknown = new InvocationResult(null, new AersError("UNKNOWN_CAPABILITY", "The capability is not registered."));
      InvocationCompleted?.Invoke(record, unknown);
      return unknown;
    }

    var definition = DummyCatalog.Get(invocation.Capability);
    var result = await definition.Behavior(new DummyBehaviorContext(
      invocation, DisplayName, AgentId, Client, Connection, options.ChildCallTimeout), cancellationToken);
    InvocationCompleted?.Invoke(record, result);
    return result;
  }

  private static string Mask(string value) =>
    value.Length <= 8 ? "********" : $"{value[..8]}...";
}

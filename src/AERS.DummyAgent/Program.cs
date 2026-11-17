using System.Text.Json;
using AERS.DummyAgent.Core.Behaviors;
using AERS.DummyAgent.Core.Scenarios;
using AERS.AgentClient;
using AERS.DummyAgent;
using Microsoft.Extensions.Configuration;
using DummyAgentInstance = AERS.DummyAgent.Core.DummyAgent;

var configuration = new ConfigurationBuilder()
  .SetBasePath(AppContext.BaseDirectory)
  .AddJsonFile("appsettings.json", optional: true)
  .AddEnvironmentVariables()
  .Build();

try
{
  var command = CommandLine.Parse(args);
  if (command.Command == "scenario")
    return RunScenario(command);
  var baseUrl = new Uri(configuration["Aers:BaseUrl"]
    ?? throw new InvalidOperationException("Aers:BaseUrl is required."));
  var registrationKey = configuration["Aers:RegistrationKey"];
  return command.Command switch
  {
    "serve" or "provider" => await ServeAsync(baseUrl, registrationKey, command),
    "call" or "consumer" => await CallAsync(baseUrl, registrationKey, command),
    "inspect" => await InspectAsync(baseUrl, command),
    _ => throw new ArgumentException($"Unknown command '{command.Command}'.")
  };
}
catch (AersApiException ex)
{
  Console.Error.WriteLine($"{ex.StatusCode} {ex.Code}: {ex.Message}");
  return 1;
}
catch (OperationCanceledException)
{
  return 0;
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or TimeoutException)
{
  Console.Error.WriteLine(ex.Message);
  return 2;
}

static async Task<int> ServeAsync(Uri baseUrl, string? registrationKey, CommandLine command)
{
  var behaviorNames = (command.Get("capability") ?? "echo,analyze_cv")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  var nextAgentId = command.Get("next-agent-id") is { } nextText ? Guid.Parse(nextText) : (Guid?)null;
  var behaviors = behaviorNames.Select(name => new DummyBehaviorOptions(
    name,
    DelayMilliseconds: command.GetInt("delay-ms", 0),
    ReconnectAfterMilliseconds: command.GetInt("reconnect-after-ms", 0),
    AnswerAfterReconnect: command.Has("answer-after-reconnect"),
    NextAgentId: nextAgentId,
    NextCapability: command.Get("next-capability") ?? "echo")).ToList();

  // Fail fast on unknown or incomplete behaviors instead of failing on the first invocation.
  foreach (var behavior in behaviors)
    DummyCatalog.Create(behavior, childCall: (_, _) => throw new InvalidOperationException());

  using var http = new HttpClient { BaseAddress = baseUrl };
  var client = new AersClient(http, new AersClientOptions { BaseUrl = baseUrl, RegistrationKey = registrationKey });
  using var cancellation = new CancellationTokenSource();
  ConsoleCancelEventHandler handler = (_, eventArgs) =>
  {
    eventArgs.Cancel = true;
    cancellation.Cancel();
  };
  Console.CancelKeyPress += handler;

  var card = new AgentCardSubmission(
    command.Get("name") ?? "Dummy Agent",
    command.Get("domain") ?? "jobhunt",
    "AERS dummy agent.",
    null,
    behaviorNames.Select(CreateCapability).ToList());
  var registration = await client.RegisterAsync(card, cancellation.Token);
  Console.WriteLine($"agent registered: {card.Name} (domain {card.Domain}) id={registration.AgentId}");

  await using var connection = new AersAgentConnection(http, new AersAgentConnectionOptions
  {
    HubUrl = new Uri(baseUrl, "hubs/agent"),
    ApiKey = registration.ApiKey
  });
  try
  {
    await connection.StartAsync(cancellation.Token);
    Console.WriteLine("hub connected");
    Func<TaskInvocation, CancellationToken, Task<InvocationResult>>? childCall = null;
    if (nextAgentId is not null)
      childCall = async (child, token) =>
      {
        var handle = await connection.SubmitAsync(
          nextAgentId.Value, child.Capability, child.Input, child.ParentTaskId, token);
        var outcome = await handle.WaitAsync(TimeSpan.FromSeconds(command.GetDouble("timeout", 30)), token);
        return new InvocationResult(outcome.Status, outcome.Output, outcome.Error);
      };
    foreach (var behavior in behaviors)
      new DummyAgentInstance(connection, behavior, registration.AgentId).Start(
        disconnect: connection.StopAsync,
        reconnect: connection.ReconnectAsync,
        childCall: childCall);
    await new AgentHeartbeatRunner(client).RunAsync(registration.AgentId, cancellation.Token);
  }
  finally
  {
    Console.CancelKeyPress -= handler;
    await client.DeregisterAsync(registration.AgentId);
    Console.WriteLine("agent deregistered.");
  }
  return 0;
}

static async Task<int> CallAsync(Uri baseUrl, string? registrationKey, CommandLine command)
{
  var capability = command.Get("capability")
    ?? throw new ArgumentException("'--capability' is required for call.");
  var domain = command.Get("domain") ?? "jobhunt";

  using var http = new HttpClient { BaseAddress = baseUrl };
  var client = new AersClient(http, new AersClientOptions { BaseUrl = baseUrl, RegistrationKey = registrationKey });
  var registration = await client.RegisterAsync(
    new AgentCardSubmission("Temporary Consumer", domain, "AERS dummy consumer.", null, [CreateCapability("consume")]));
  await using var connection = new AersAgentConnection(http, new AersAgentConnectionOptions
  {
    HubUrl = new Uri(baseUrl, "hubs/agent"),
    ApiKey = registration.ApiKey
  });
  try
  {
    await connection.StartAsync();
    int? limit = command.Has("page-size") ? command.GetInt("page-size", 50) : null;
    var query = new AgentQuery(command.Get("query-domain") ?? domain, capability, command.Has("online-only"), limit);
    AgentCard? target = null;
    await foreach (var candidate in client.ListAllAgentsAsync(query))
    {
      if (candidate.AgentId != registration.AgentId && candidate.Capabilities.Any(item => item.Name == capability))
      {
        target = candidate;
        break;
      }
    }
    if (target is null)
      throw new InvalidOperationException($"No agent offering '{capability}' was found.");
    Console.WriteLine($"call: discovered Agent {target.AgentId} offering {capability} in domain {query.Domain}");

    using var inputDocument = JsonDocument.Parse(command.Get("input") ?? "{}");
    var parent = command.Get("parent-task-id") is { } parentText ? Guid.Parse(parentText) : (Guid?)null;
    var handle = await connection.SubmitAsync(target.AgentId, capability, inputDocument.RootElement.Clone(), parent);
    Console.WriteLine($"call: task={handle.TaskId} submitted (status {handle.Status})");
    var outcome = await handle.WaitAsync(TimeSpan.FromSeconds(command.GetDouble("timeout", 10)));
    Console.WriteLine($"call: task={handle.TaskId} status={outcome.Status} output={outcome.Output?.GetRawText() ?? "null"}");
    if (outcome.Error is not null)
      Console.WriteLine($"call: task={handle.TaskId} code={outcome.Error.Code} message={outcome.Error.Message}");
    return outcome.Status == "completed" ? 0 : 1;
  }
  finally
  {
    await client.DeregisterAsync(registration.AgentId);
  }
}

static async Task<int> InspectAsync(Uri baseUrl, CommandLine command)
{
  if (!Guid.TryParse(command.Get("task-id"), out var taskId))
    throw new ArgumentException("'--task-id' must be a valid task id.");
  var apiKey = Environment.GetEnvironmentVariable(command.Get("api-key-env") ?? "AERS_API_KEY")
    ?? throw new InvalidOperationException("The API key environment variable is not set.");
  using var http = new HttpClient();
  var client = new AersClient(http, new AersClientOptions { BaseUrl = baseUrl, ApiKey = apiKey });
  var task = await client.GetTaskAsync(taskId);
  Console.WriteLine(JsonSerializer.Serialize(task, new JsonSerializerOptions { WriteIndented = true }));
  return 0;
}

static int RunScenario(CommandLine command)
{
  var name = command.Positional.Count == 1
    ? command.Positional[0]
    : command.Get("name") ?? throw new ArgumentException("Usage: scenario <name>.");
  var result = new ScenarioRunner().RunDry(name);
  Console.WriteLine(result.Message);
  return result.Passed ? 0 : 1;
}

static Capability CreateCapability(string name)
{
  using var schema = JsonDocument.Parse("""{"type":"object"}""");
  return new Capability(name, $"Dummy capability '{name}'.", schema.RootElement.Clone(), schema.RootElement.Clone());
}

using System.Text.Json;
using AERS.AgentClient;
using AERS.DummyAgent;
using AERS.DummyAgent.Core;
using AERS.DummyAgent.Core.Scenarios;
using Microsoft.Extensions.Configuration;

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
    "scenario" => await ScenarioAsync(baseUrl, registrationKey, command),
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
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
{
  Console.Error.WriteLine(ex.Message);
  return 2;
}

static async Task<int> ServeAsync(Uri baseUrl, string? registrationKey, CommandLine command)
{
  var capabilities = (command.Get("capability") ?? "echo,analyze_cv")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  var agent = new DummyAgent(new(
    baseUrl,
    registrationKey,
    command.Get("name") ?? "Dummy Agent",
    command.Get("domain") ?? "jobhunt",
    capabilities)
  {
    Connect = !command.Has("no-connect")
  });
  agent.InvocationCompleted += (record, result) =>
  {
    Console.WriteLine($"invoke: task={record.TaskId} capability={record.Capability} depth={record.Depth} parent={record.ParentTaskId?.ToString() ?? "-"}");
    Console.WriteLine($"result: task={record.TaskId} status={(result.Error is null ? "completed" : "failed")}");
  };

  using var cancellation = new CancellationTokenSource();
  ConsoleCancelEventHandler handler = (_, eventArgs) =>
  {
    eventArgs.Cancel = true;
    cancellation.Cancel();
  };
  Console.CancelKeyPress += handler;
  try
  {
    await agent.StartAsync(cancellation.Token);
    Console.WriteLine($"agent registered: {agent.DisplayName} (domain {command.Get("domain") ?? "jobhunt"}) id={agent.AgentId}, key={agent.MaskedApiKey}");
    if (!command.Has("no-connect"))
      Console.WriteLine("hub connected");
    await Task.Delay(Timeout.InfiniteTimeSpan, cancellation.Token);
  }
  finally
  {
    Console.CancelKeyPress -= handler;
    await agent.StopAsync();
    Console.WriteLine("agent deregistered.");
  }
  return 0;
}

static async Task<int> CallAsync(Uri baseUrl, string? registrationKey, CommandLine command)
{
  var capability = command.Get("capability")
    ?? throw new ArgumentException("'--capability' is required for call.");
  var domain = command.Get("domain") ?? "jobhunt";
  var consumer = new DummyAgent(new(baseUrl, registrationKey, "Temporary Consumer", domain, []));
  try
  {
    await consumer.StartAsync();
    var query = new AgentQuery(command.Get("query-domain") ?? domain, capability, command.Has("online-only"), null);
    AgentCard? target = null;
    await foreach (var candidate in consumer.Client.ListAllAgentsAsync(query))
    {
      if (candidate.AgentId != consumer.AgentId &&
          candidate.Capabilities.Any(item => item.Name == capability))
      {
        target = candidate;
        break;
      }
    }
    if (target is null)
      throw new InvalidOperationException($"No agent offering '{capability}' was found.");
    Console.WriteLine($"call: discovered Agent {target.AgentId} offering {capability} in domain {query.Domain}");
    var input = command.Get("input") is { } raw ? Element(raw) : Element("{}");
    var timeout = TimeSpan.FromSeconds(command.GetDouble("timeout", 10));
    var parent = command.Get("parent-task-id") is { } parentText
      ? Guid.Parse(parentText)
      : (Guid?)null;
    var ack = await consumer.Connection.SubmitAsync(new CreateTaskCall(target.AgentId, capability, input, parent));
    Console.WriteLine($"call: task={ack.TaskId} submitted (status {ack.Status})");
    var outcome = await consumer.Connection.WaitForResultAsync(ack.TaskId!.Value, timeout);
    Console.WriteLine($"call: task={outcome.TaskId} status={outcome.Status} output={outcome.Output?.GetRawText() ?? "null"}");
    if (outcome.Error is not null)
      Console.WriteLine($"call: task={outcome.TaskId} status=failed code={outcome.Error.Code} message={outcome.Error.Message}");
    return outcome.Status == TaskStatusValue.Completed ? 0 : 1;
  }
  finally
  {
    await consumer.StopAsync();
  }
}

static async Task<int> ScenarioAsync(Uri baseUrl, string? registrationKey, CommandLine command)
{
  var name = command.Get("name") ?? throw new ArgumentException("'scenario' requires '--name'.");
  var result = await new ScenarioRunner().RunAsync(name, new(baseUrl, registrationKey));
  Console.WriteLine($"{(result.Passed ? "PASS" : "FAIL")} {result.Name}");
  foreach (var fact in result.Facts)
    Console.WriteLine($"fact: {fact}");
  foreach (var failure in result.Failures)
    Console.WriteLine($"failure: {failure}");
  return result.Passed ? 0 : 1;
}

static int RunScenario(CommandLine command)
{
  if (command.Positional.Count != 1)
    throw new ArgumentException("Usage: scenario <name>.");
  var result = new ScenarioRunner().RunDry(command.Positional[0]);
  Console.WriteLine(result.Message);
  return result.Passed ? 0 : 1;
}

static AgentCardSubmission CreateCard(
  string name, string domain, Capability capability) =>
  new(name, domain, "AERS Week 5 dummy agent.", null, [capability]);

static Capability EmptyCapability(string name) =>
  new(name, "Seed or consumer capability.", JsonDocument.Parse("{}").RootElement.Clone(),
    JsonDocument.Parse("{}").RootElement.Clone());

static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

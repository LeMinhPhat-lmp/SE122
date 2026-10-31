using System.Text.Json;
using AERS.AgentClient;
using AERS.DummyAgent;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
  .SetBasePath(AppContext.BaseDirectory)
  .AddJsonFile("appsettings.json", optional: true)
  .AddEnvironmentVariables()
  .Build();

try
{
  var command = CommandLine.Parse(args);
  var baseUrl = new Uri(configuration["Aers:BaseUrl"]
    ?? throw new InvalidOperationException("Aers:BaseUrl is required."));
  var registrationKey = configuration["Aers:RegistrationKey"];
  using var http = new HttpClient { BaseAddress = baseUrl };
  var client = new AersClient(http, new AersClientOptions
  {
    BaseUrl = baseUrl,
    RegistrationKey = registrationKey
  });

  switch (command.Command)
  {
    case "provider":
      await RunProviderAsync(client, command);
      break;
    case "consumer":
      await RunConsumerAsync(client, command);
      break;
    case "seed":
      await RunSeedAsync(client, command);
      break;
    default:
      throw new ArgumentException($"Unknown command '{command.Command}'.");
  }
}
catch (AersApiException ex)
{
  Console.Error.WriteLine($"{ex.Code}: {ex.Message}");
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

return 0;

static async Task RunProviderAsync(AersClient client, CommandLine command)
{
  var capabilityName = command.Get("capability") ?? "analyze_cv";
  var agent = await client.RegisterAsync(CreateCard(
    command.Get("name") ?? "CV Analysis Agent",
    command.Get("domain") ?? "jobhunt",
    new Capability(
      capabilityName,
      "Analyze a CV against a job.",
      JsonDocument.Parse("""{"type":"object","properties":{"cvText":{"type":"string"}},"required":["cvText"]}""").RootElement.Clone(),
      JsonDocument.Parse("""{"type":"object","properties":{"score":{"type":"number"}}}""").RootElement.Clone())));

  using var cancellation = new CancellationTokenSource();
  ConsoleCancelEventHandler handler = (_, eventArgs) =>
  {
    eventArgs.Cancel = true;
    cancellation.Cancel();
  };
  Console.CancelKeyPress += handler;
  Console.WriteLine($"registered provider {agent.AgentId}; apiKey={Mask(agent.ApiKey)}");
  try
  {
    await new AgentHeartbeatRunner(client).RunAsync(agent.AgentId, cancellation.Token);
  }
  finally
  {
    Console.CancelKeyPress -= handler;
    await client.DeregisterAsync(agent.AgentId);
    Console.WriteLine("provider deregistered.");
  }
}

static async Task RunConsumerAsync(AersClient client, CommandLine command)
{
  var requestedCapability = command.Get("capability") ?? "analyze_cv";
  var registration = await client.RegisterAsync(CreateCard(
    "Discovery Consumer",
    command.Get("domain") ?? "jobhunt",
    EmptyCapability("consumer_probe")));
  Console.WriteLine($"registered consumer {registration.AgentId}; apiKey={Mask(registration.ApiKey)}");
  try
  {
    var pageSize = command.Get("page-size");
    var query = new AgentQuery(
      command.Get("query-domain"),
      requestedCapability,
      command.Has("online-only"),
      pageSize is null ? null : command.GetInt("page-size", 20));
    var candidates = new List<AgentCard>();
    var page = 0;
    await foreach (var agent in client.ListAllAgentsAsync(query, (number, count) =>
    {
      page = number;
      if (pageSize is not null)
        Console.WriteLine($"page {number}: {count} agents");
    }))
      candidates.Add(agent);

    var selected = candidates.FirstOrDefault(agent =>
      agent.Capabilities.Any(capability => capability.Name == requestedCapability));
    if (selected is null)
    {
      Console.WriteLine("No agent with the requested capability was found.");
      return;
    }

    var capabilities = await client.GetCapabilitiesAsync(selected.AgentId);
    var capability = capabilities.Capabilities.First(item => item.Name == requestedCapability);
    Console.WriteLine($"found {selected.Name} ({selected.AgentId})");
    Console.WriteLine($"inputSchema: {capability.InputSchema.GetRawText()}");
    Console.WriteLine($"outputSchema: {capability.OutputSchema.GetRawText()}");

    var validation = SchemaValidation.Validate(
      capability.InputSchema,
      command.Get("input") ?? """{"cvText":"sample"}""");
    Console.WriteLine(validation.IsValid ? "input valid" : $"input invalid: {string.Join("; ", validation.Errors)}");
  }
  finally
  {
    await client.DeregisterAsync(registration.AgentId);
  }
}

static async Task RunSeedAsync(AersClient client, CommandLine command)
{
  var count = command.GetInt("count", 0);
  if (count < 1)
    throw new ArgumentException("'--count' must be greater than zero.");

  var domain = command.Get("domain") ?? "jobhunt";
  for (var index = 1; index <= count; index++)
  {
    await client.RegisterAsync(CreateCard(
      $"Seed Agent {index:000}",
      domain,
      EmptyCapability("seed_capability")));
  }

  Console.WriteLine($"registered {count} seed agents.");
}

static AgentCardSubmission CreateCard(
  string name, string domain, Capability capability) =>
  new(name, domain, "AERS Week 5 dummy agent.", null, [capability]);

static Capability EmptyCapability(string name) =>
  new(name, "Seed or consumer capability.", JsonDocument.Parse("{}").RootElement.Clone(),
    JsonDocument.Parse("{}").RootElement.Clone());

static string Mask(string value) =>
  value.Length <= 8 ? "********" : $"{value[..8]}...";

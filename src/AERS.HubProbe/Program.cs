// Manual-testing harness for the AERS hub. Talks to the server only over the network.
//
//   serve --url http://localhost:5161 --key <apiKey> --behavior echo|fail|slow|forward [--forward-target <agentId>] [--delay-ms 3000]
//   call  --url http://localhost:5161 --key <apiKey> --target <agentId> --capability echo --input '{"message":"hi"}' [--parent <taskId>] [--wait 15]
//   call  ... --type <messageType> --raw '<payload json>'      (negative tests, sends the payload exactly as given)
//
// "serve" connects as an agent, answers every task.invoke according to --behavior and stays connected until Ctrl+C.
// "call" connects as an agent, sends one task.create, prints the ack and waits for the pushed task.result.
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

var options = ParseArguments(args, out var command);
if (command is not ("serve" or "call") || !options.ContainsKey("url") || !options.ContainsKey("key"))
{
  Console.WriteLine("Usage: serve|call --url <baseUrl> --key <apiKey> [command options]. See the header of Program.cs.");
  return 1;
}

var pending = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>();
TaskCompletionSource<JsonElement> ResultFor(string taskId) =>
  pending.GetOrAdd(taskId, _ => new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously));

var connection = new HubConnectionBuilder()
  .WithUrl(options["url"].TrimEnd('/') + "/hubs/agent", http => http.Headers["X-Api-Key"] = options["key"])
  .AddJsonProtocol(json => json.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
  .Build();

var behavior = options.GetValueOrDefault("behavior", "echo");

// Handlers run on a separate task: the client processes messages one at a time, so a handler that waits for another
// message (the "forward" behavior waits for a child result) would otherwise block the receive loop forever.
connection.On<JsonElement>("Receive", envelope => { _ = Task.Run(() => OnReceiveAsync(envelope)); });

try
{
  await connection.StartAsync();
}
catch (Exception ex)
{
  Console.WriteLine($"CONNECT FAILED: {ex.GetType().Name}: {ex.Message}");
  return 2;
}

Console.WriteLine($"CONNECTED as the agent that owns this key (connection id {connection.ConnectionId}).");

if (command == "serve")
{
  Console.WriteLine($"Serving with behavior '{behavior}'. Press Ctrl+C to stop.");
  await Task.Delay(Timeout.Infinite);
  return 0;
}

// Optional --type and --raw send an arbitrary envelope (negative tests): for example --type task.result --raw '{"taskId":"…"}'.
var outgoingType = options.GetValueOrDefault("type", "task.create");
object outgoingPayload;
if (options.TryGetValue("raw", out var raw))
{
  outgoingPayload = JsonDocument.Parse(raw).RootElement.Clone();
}
else
{
  outgoingPayload = new
  {
    targetAgentId = options["target"],
    capability = options.GetValueOrDefault("capability", "echo"),
    input = JsonDocument.Parse(options.GetValueOrDefault("input", "{}")).RootElement.Clone(),
    parentTaskId = options.GetValueOrDefault("parent")
  };
}

var ack = await SendAsync(outgoingType, outgoingPayload);
Print("ACK", ack);

// Only a task.create produces a pushed task.result for this connection.
if (outgoingType == "task.create" && ack.GetProperty("accepted").GetBoolean() && ack.GetProperty("taskId").GetString() is { } createdTaskId)
{
  var wait = TimeSpan.FromSeconds(int.Parse(options.GetValueOrDefault("wait", "15")));
  var finished = await Task.WhenAny(ResultFor(createdTaskId).Task, Task.Delay(wait));
  if (finished == ResultFor(createdTaskId).Task)
    Print("RESULT (pushed)", ResultFor(createdTaskId).Task.Result);
  else
    Console.WriteLine($"NO RESULT within {wait.TotalSeconds}s (the task stays 'working'; read it with GET /tasks/{createdTaskId}).");
}

await connection.StopAsync();
return 0;

// ---------------------------------------------------------------------------------------------------------------

async Task<JsonElement> SendAsync(string messageType, object payload)
{
  var payloadElement = JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
  return await connection.InvokeAsync<JsonElement>("Send", new { messageType, correlationId = (string?)null, payload = payloadElement });
}

async Task OnReceiveAsync(JsonElement envelope)
{
  var messageType = envelope.GetProperty("messageType").GetString();
  var payload = envelope.GetProperty("payload");
  Print($"RECEIVED {messageType}", payload);

  if (messageType == "task.result")
  {
    ResultFor(payload.GetProperty("taskId").GetString()!).TrySetResult(payload.Clone());
    return;
  }

  if (messageType != "task.invoke")
    return;

  var taskId = payload.GetProperty("taskId").GetString()!;
  var taskInput = payload.GetProperty("input");

  object result;
  switch (behavior)
  {
    case "fail":
      result = new { taskId, status = "failed", output = (object?)null, error = new { code = "DUMMY_FAILURE", message = "Failure requested by the probe." } };
      break;

    case "slow":
      await Task.Delay(int.Parse(options.GetValueOrDefault("delay-ms", "3000")));
      result = new { taskId, status = "completed", output = new { echo = taskInput }, error = (object?)null };
      break;

    case "forward":
      var childAck = await SendAsync("task.create", new
      {
        targetAgentId = options["forward-target"],
        capability = "echo",
        input = taskInput,
        parentTaskId = taskId
      });
      Print("CHILD ACK", childAck);
      if (!childAck.GetProperty("accepted").GetBoolean())
      {
        result = new { taskId, status = "failed", output = (object?)null, error = new { code = "CHILD_REJECTED", message = "The child task was rejected." } };
        break;
      }

      var childResult = await ResultFor(childAck.GetProperty("taskId").GetString()!).Task.WaitAsync(TimeSpan.FromSeconds(20));
      result = childResult.GetProperty("status").GetString() == "completed"
        ? new { taskId, status = "completed", output = new { forwarded = childResult.GetProperty("output") }, error = (object?)null }
        : new { taskId, status = "failed", output = (object?)null, error = new { code = "CHILD_FAILED", message = "The child task failed." } };
      break;

    default:
      result = new { taskId, status = "completed", output = new { echo = taskInput }, error = (object?)null };
      break;
  }

  Print("SEND task.result ack", await SendAsync("task.result", result));
}

void Print(string title, JsonElement value) =>
  Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {title}: {JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false })}");

static Dictionary<string, string> ParseArguments(string[] args, out string command)
{
  command = args.Length > 0 ? args[0] : string.Empty;
  var parsed = new Dictionary<string, string>();
  for (var i = 1; i + 1 < args.Length; i += 2)
    parsed[args[i].TrimStart('-')] = args[i + 1];
  return parsed;
}

using System.Text.Json;
using AERS.AgentClient;

namespace AERS.DummyAgent.Core.Behaviors;

public sealed record DummyBehaviorContext(
  TaskInvocation Invocation,
  string DisplayName,
  Guid AgentId,
  AersClient Client,
  AersAgentConnection Connection,
  TimeSpan ChildCallTimeout);

public delegate Task<InvocationResult> DummyBehavior(
  DummyBehaviorContext context,
  CancellationToken cancellationToken);

public sealed record CapabilityDefinition(
  string Name,
  string Description,
  JsonElement InputSchema,
  JsonElement OutputSchema,
  DummyBehavior Behavior);

public static class DummyCatalog
{
  private static readonly IReadOnlyDictionary<string, CapabilityDefinition> Definitions =
    new Dictionary<string, CapabilityDefinition>(StringComparer.Ordinal)
    {
      ["echo"] = Create("echo", "Echoes a message.", """{"type":"object","additionalProperties":false,"required":["message"],"properties":{"message":{"type":"string"}}}""",
        """{"type":"object","additionalProperties":false,"required":["message","handledBy"],"properties":{"message":{"type":"string"},"handledBy":{"type":"string"}}}""", EchoAsync),
      ["analyze_cv"] = Create("analyze_cv", "Analyzes CV text.", """{"type":"object","additionalProperties":false,"required":["cvText"],"properties":{"cvText":{"type":"string","minLength":1}}}""",
        """{"type":"object","additionalProperties":false,"required":["score","wordCount"],"properties":{"score":{"type":"number"},"wordCount":{"type":"integer"}}}""", AnalyzeCvAsync),
      ["slow_echo"] = Create("slow_echo", "Echoes a message after a delay.", """{"type":"object","additionalProperties":false,"required":["message","delayMs"],"properties":{"message":{"type":"string"},"delayMs":{"type":"integer","minimum":0,"maximum":60000}}}""",
        """{"type":"object","additionalProperties":false,"required":["message"],"properties":{"message":{"type":"string"}}}""", SlowEchoAsync),
      ["always_fail"] = Create("always_fail", "Always returns a deterministic failure.", """{"type":"object","additionalProperties":false,"properties":{"reason":{"type":"string"}}}""",
        """{"type":"object","additionalProperties":false}""", AlwaysFailAsync),
      ["never_reply"] = Create("never_reply", "Never returns a result.", """{"type":"object","additionalProperties":false}""",
        """{"type":"object","additionalProperties":false}""", NeverReplyAsync),
      ["forward"] = Create("forward", "Forwards a task to another agent.", """{"type":"object","additionalProperties":false,"required":["targetCapability","input"],"properties":{"targetCapability":{"type":"string"},"input":{"type":"object"},"domain":{"type":"string"}}}""",
        """{"type":"object","additionalProperties":false,"required":["forwardedTo","result"],"properties":{"forwardedTo":{"type":"string"},"result":{"type":"object"}}}""", ForwardAsync)
    };

  public static IReadOnlyList<CapabilityDefinition> All { get; } = Definitions.Values.ToArray();

  public static CapabilityDefinition Get(string name) =>
    Definitions.TryGetValue(name, out var definition)
      ? definition
      : throw new KeyNotFoundException($"Unknown dummy capability '{name}'.");

  private static CapabilityDefinition Create(
    string name,
    string description,
    string inputSchema,
    string outputSchema,
    DummyBehavior behavior) =>
    new(name, description, Element(inputSchema), Element(outputSchema), behavior);

  private static Task<InvocationResult> EchoAsync(DummyBehaviorContext context, CancellationToken _)
  {
    var message = context.Invocation.Input.GetProperty("message").GetString()!;
    return Task.FromResult(new InvocationResult(Element($$"""{"message":{{JsonSerializer.Serialize(message)}},"handledBy":{{JsonSerializer.Serialize(context.DisplayName)}}}"""), null));
  }

  private static Task<InvocationResult> AnalyzeCvAsync(DummyBehaviorContext context, CancellationToken _)
  {
    var text = context.Invocation.Input.GetProperty("cvText").GetString()!;
    var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    var score = (text.Length * 7) % 101;
    return Task.FromResult(new InvocationResult(Element($$"""{"score":{{score}},"wordCount":{{wordCount}}}"""), null));
  }

  private static async Task<InvocationResult> SlowEchoAsync(DummyBehaviorContext context, CancellationToken cancellationToken)
  {
    var delay = context.Invocation.Input.GetProperty("delayMs").GetInt32();
    await Task.Delay(delay, cancellationToken);
    var message = context.Invocation.Input.GetProperty("message").GetString()!;
    return new InvocationResult(Element($$"""{"message":{{JsonSerializer.Serialize(message)}}}"""), null);
  }

  private static Task<InvocationResult> AlwaysFailAsync(DummyBehaviorContext context, CancellationToken _)
  {
    var reason = context.Invocation.Input.TryGetProperty("reason", out var value)
      ? value.GetString()
      : null;
    return Task.FromResult(new InvocationResult(null,
      new AersError("DUMMY_FAILURE", string.IsNullOrWhiteSpace(reason) ? "always_fail invoked" : reason)));
  }

  private static Task<InvocationResult> NeverReplyAsync(DummyBehaviorContext _, CancellationToken __) =>
    Task.FromResult(new InvocationResult(null, null, SuppressReply: true));

  private static async Task<InvocationResult> ForwardAsync(
    DummyBehaviorContext context,
    CancellationToken cancellationToken)
  {
    var targetCapability = context.Invocation.Input.GetProperty("targetCapability").GetString()!;
    var input = context.Invocation.Input.GetProperty("input").Clone();
    var domain = context.Invocation.Input.TryGetProperty("domain", out var domainElement)
      ? domainElement.GetString()
      : context.Invocation.PolicyContext.TargetDomain;
    var query = new AgentQuery(domain, targetCapability, true, null);
    AgentCard? target = null;
    await foreach (var candidate in context.Client.ListAllAgentsAsync(query, cancellationToken))
    {
      if (candidate.AgentId != context.AgentId &&
          candidate.Capabilities.Any(capability => capability.Name == targetCapability))
      {
        target = candidate;
        break;
      }
    }

    if (target is null)
      return new InvocationResult(null, new AersError("NO_FORWARD_TARGET", "No forwarding target was found."));

    TaskOutcome child;
    try
    {
      child = await context.Connection.CallAsync(
        new CreateTaskCall(target.AgentId, targetCapability, input, context.Invocation.TaskId),
        context.ChildCallTimeout,
        cancellationToken);
    }
    catch (TaskResultTimeoutException)
    {
      return new InvocationResult(null, new AersError("CHILD_TIMEOUT", "The forwarded task timed out."));
    }

    if (child.Status != TaskStatusValue.Completed)
    {
      var details = JsonSerializer.SerializeToElement(new
      {
        childCode = child.Error?.Code,
        childMessage = child.Error?.Message
      });
      return new InvocationResult(null,
        new AersError("CHILD_TASK_FAILED", child.Error?.Message ?? "The forwarded task failed.", details));
    }

    var output = JsonSerializer.SerializeToElement(new
    {
      forwardedTo = target.AgentId,
      result = child.Output ?? Element("{}")
    });
    return new InvocationResult(output, null);
  }

  private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

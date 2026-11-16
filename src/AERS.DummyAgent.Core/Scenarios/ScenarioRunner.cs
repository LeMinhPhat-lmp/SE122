using System.Text.Json;
using AERS.AgentClient;
using AERS.DummyAgent.Core.Behaviors;

namespace AERS.DummyAgent.Core.Scenarios;

public sealed record ScenarioRunnerOptions(
  Uri BaseUrl,
  string? RegistrationKey);

public sealed record ScenarioResult(
  string Name,
  bool Passed,
  IReadOnlyList<string> Facts,
  IReadOnlyList<string> Failures);

public sealed class ScenarioRunner
{
  public async Task<ScenarioResult> RunAsync(
    string name,
    ScenarioRunnerOptions options,
    CancellationToken cancellationToken = default)
  {
    var facts = new List<string>();
    var failures = new List<string>();
    var agents = new List<DummyAgent>();
    try
    {
      var suffix = Guid.NewGuid().ToString("N")[..8];
      var domain = $"demo-{suffix}";
      var scenario = name.ToLowerInvariant();
      string[] targetCapabilities = scenario switch
      {
        "sync-ab" or "target-unavailable" or "invalid-input" => ["analyze_cv"],
        "target-failure" => ["always_fail"],
        "child-failure" or "chain-abc" => ["forward"],
        _ => throw new ArgumentException($"Unknown scenario '{name}'.")
      };
      string[] childCapabilities = scenario switch
      {
        "child-failure" => ["always_fail"],
        "chain-abc" => ["analyze_cv"],
        _ => ["analyze_cv"]
      };

      var caller = NewAgent(options, $"Scenario caller {suffix}", domain, [], true);
      var target = NewAgent(options, $"Scenario target {suffix}", domain, targetCapabilities,
        scenario != "target-unavailable");
      agents.Add(caller);
      agents.Add(target);
      await caller.StartAsync(cancellationToken);
      await target.StartAsync(cancellationToken);
      DummyAgent? child = null;
      if (scenario is "child-failure" or "chain-abc")
      {
        child = NewAgent(options, $"Scenario child {suffix}", domain, childCapabilities, true);
        agents.Add(child);
        await child.StartAsync(cancellationToken);
      }

      AgentCard? targetCard = null;
      await foreach (var card in caller.Client.ListAllAgentsAsync(
        new(domain, targetCapabilities[0], scenario != "target-unavailable", null), cancellationToken))
      {
        if (card.AgentId == target.AgentId)
        {
          targetCard = card;
          break;
        }
      }
      if (targetCard is null)
        throw new InvalidOperationException("The scenario target was not discovered.");
      var input = scenario switch
      {
        "invalid-input" => Element("{}"),
        "target-failure" => Element("""{"reason":"scenario failure"}"""),
        "child-failure" => Element("""{"targetCapability":"always_fail","input":{"reason":"child failure"}}"""),
        "chain-abc" => Element("""{"targetCapability":"analyze_cv","input":{"cvText":"hello world"}}"""),
        _ => Element("""{"cvText":"one two three"}""")
      };
      try
      {
        var outcome = await caller.Connection.CallAsync(
          new CreateTaskCall(targetCard.AgentId, targetCapabilities[0], input),
          TimeSpan.FromSeconds(10),
          cancellationToken);
        facts.Add($"status={outcome.Status}");
        var expectsFailure = scenario is "target-unavailable" or "target-failure" or "child-failure";
        if (expectsFailure && outcome.Status != TaskStatusValue.Failed)
          failures.Add("Expected a failed outcome.");
        else if (!expectsFailure && outcome.Status != TaskStatusValue.Completed)
          failures.Add("Expected a completed outcome.");
      }
      catch (AersApiException ex) when (scenario == "invalid-input" && ex.Code == "VALIDATION_ERROR")
      {
        facts.Add("validation error returned");
      }

      if (target.Invocations.Count == 0 && scenario != "invalid-input" && scenario != "target-unavailable")
        failures.Add("Target did not record an invocation.");
    }
    catch (Exception ex)
    {
      failures.Add($"{ex.GetType().Name}: {ex.Message}");
    }
    finally
    {
      foreach (var agent in agents.AsEnumerable().Reverse())
        await agent.StopAsync(cancellationToken);
    }

    return new ScenarioResult(name, failures.Count == 0, facts, failures);
  }

  private static DummyAgent NewAgent(
    ScenarioRunnerOptions options,
    string name,
    string domain,
    IReadOnlyList<string> capabilities,
    bool connect) =>
    new(new(options.BaseUrl, options.RegistrationKey, name, domain, capabilities) { Connect = connect });

  private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();
}

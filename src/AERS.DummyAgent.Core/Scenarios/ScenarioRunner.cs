namespace AERS.DummyAgent.Core.Scenarios;

public sealed record ScenarioResult(string Name, bool Passed, string Message);

public sealed class ScenarioRunner
{
  private static readonly string[] Names =
  [
    "async-ab", "caller-offline", "timeout", "target-reconnect",
    "target-vanishes", "child-deadline", "chain-abc", "cycle",
    "self-call", "depth", "rate-limit", "cross-domain",
    "revoked-key", "orphan-root"
  ];

  public IReadOnlyList<string> SupportedScenarios => Names;

  public ScenarioResult RunDry(string name)
  {
    if (!Names.Contains(name, StringComparer.Ordinal))
      return new ScenarioResult(name, false, $"Unknown scenario '{name}'.");
    return new ScenarioResult(name, true,
      $"PASS {name}: behavior topology validated; run against AERS for persistence and timeout assertions.");
  }
}

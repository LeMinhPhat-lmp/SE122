namespace Application.Options;

public sealed class AersPolicyOptions
{
  public const string SectionName = "Aers:Policy";
  public HeartbeatOptions Heartbeat { get; set; } = new();
  public DomainPolicyOptions DomainPolicy { get; set; } = new();

  public sealed class HeartbeatOptions
  {
    public int IntervalSeconds { get; set; } = 15;
    public int OfflineThresholdSeconds { get; set; } = 45;
  }

  public sealed class DomainPolicyOptions
  {
    public string Mode { get; set; } = "DenyByDefault";
    public List<CrossDomainRule> CrossDomainRules { get; set; } = new();
  }

  public sealed class CrossDomainRule
  {
    public string CallerDomain { get; set; } = string.Empty;
    public string TargetDomain { get; set; } = string.Empty;
  }
}

namespace Application.Options;

public sealed class AersPolicyOptions
{
  public const string SectionName = "Aers:Policy";
  public HeartbeatOptions Heartbeat { get; set; } = new();
  public DomainPolicyOptions DomainPolicy { get; set; } = new();

  /// <summary>A task is rejected with DEPTH_EXCEEDED when its depth is greater than this value (A54), so a chain of at most MaxRecursionDepth + 1 tasks is allowed.</summary>
  public int MaxRecursionDepth { get; set; } = 5;

  public RateLimitOptions RateLimit { get; set; } = new();

  public sealed class RateLimitOptions
  {
    public int WindowSeconds { get; set; } = 60;
    public int MaxTasksPerWindow { get; set; } = 100;
  }

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

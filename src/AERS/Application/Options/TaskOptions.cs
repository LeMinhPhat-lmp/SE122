namespace Application.Options;

public sealed class TaskOptions
{
  public const string SectionName = "Aers:Tasks";

  /// <summary>Stored as deadline_at = created_at + timeout (a child task never gets a later deadline than its parent, A47) and enforced by the timeout worker.</summary>
  public int DefaultTaskTimeoutSeconds { get; set; } = 300;
  public int MaxInputBytes { get; set; } = 262144;
  public int MaxOutputBytes { get; set; } = 262144;

  /// <summary>Pause between two timeout worker ticks (A46).</summary>
  public int TimeoutScanIntervalSeconds { get; set; } = 5;

  /// <summary>Maximum number of tasks or agents handled per query of the timeout worker (A46).</summary>
  public int TimeoutBatchSize { get; set; } = 100;

  /// <summary>Added to the remaining time until the deadline for the TTL of a non-terminal runtime state (A44).</summary>
  public int RuntimeStateGraceSeconds { get; set; } = 60;

  /// <summary>TTL of the runtime state once the task is completed or failed (A44).</summary>
  public int RuntimeStateTerminalTtlSeconds { get; set; } = 60;

  /// <summary>Maximum number of unacknowledged results pushed per reconnect (ADR-W7-02).</summary>
  public int RedeliveryBatchSize { get; set; } = 100;

  /// <summary>How long a target may stay disconnected before its working tasks fail with TARGET_UNAVAILABLE (ADR-W7-03).</summary>
  public int TargetDisconnectGraceSeconds { get; set; } = 30;
}

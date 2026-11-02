namespace Application.Options;

public sealed class TaskOptions
{
  public const string SectionName = "Aers:Tasks";

  /// <summary>Stored as deadline_at = created_at + timeout. Not enforced in Week 6 (timeout worker is Week 7).</summary>
  public int DefaultTaskTimeoutSeconds { get; set; } = 300;
  public int MaxInputBytes { get; set; } = 262144;
  public int MaxOutputBytes { get; set; } = 262144;
}

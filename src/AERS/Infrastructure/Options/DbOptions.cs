namespace Infrastructure.Options;

public sealed class DbOptions
{
  public const string SectionName = "Database";
  public string ConnectionString { get; init; } = string.Empty;
  public int CommandTimeoutSeconds { get; init; } = 30;
  public bool EnableSensitiveDataLogging { get; init; } = false;
  public bool EnableDetailedErrors { get; init; } = false;
}
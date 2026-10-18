namespace Application.Options;

public sealed class RegistrationLimitsOptions
{
  public const string SectionName = "Aers:RegistrationLimits";
  public int MaxNameLength { get; set; } = 200;
  public int MaxCapabilities { get; set; } = 50;
  public int MaxSchemaBytes { get; set; } = 65536;
  public int MaxBodyBytes { get; set; } = 1048576;
}

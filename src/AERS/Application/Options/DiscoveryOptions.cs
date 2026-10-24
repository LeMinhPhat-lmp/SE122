namespace Application.Options;

public sealed class DiscoveryOptions
{
  public const string SectionName = "Aers:Discovery";
  public int DefaultPageSize { get; set; } = 50;
  public int MaxPageSize { get; set; } = 100;
}

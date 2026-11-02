namespace Application.Options;

public sealed class InstanceOptions
{
  public const string SectionName = "Aers:Instance";

  /// <summary>Identifier of this AERS instance. An empty value falls back to Environment.MachineName (see DI registration).</summary>
  public string Id { get; set; } = string.Empty;
}

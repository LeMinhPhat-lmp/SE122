namespace Application.Options;

public sealed class RelayOptions
{
  public const string SectionName = "Aers:Relay";

  /// <summary>SignalR maximum receive message size in bytes.</summary>
  public int MaxMessageBytes { get; set; } = 1048576;
}

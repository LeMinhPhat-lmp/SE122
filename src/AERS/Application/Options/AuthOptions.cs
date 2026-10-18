namespace Application.Options;

public sealed class AuthOptions
{
  public const string SectionName = "Aers:Auth";
  public List<string> RegistrationKeys { get; set; } = new();
}

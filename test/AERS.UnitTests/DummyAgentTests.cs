using System.Text.Json;
using AERS.DummyAgent;
using Xunit;

namespace AERS.UnitTests;

public sealed class DummyAgentTests
{
  [Fact]
  public void CommandLine_ParsesBooleanAndValues()
  {
    var command = CommandLine.Parse(["consumer", "--capability", "analyze_cv", "--online-only", "--page-size", "20"]);

    Assert.Equal("consumer", command.Command);
    Assert.Equal("analyze_cv", command.Get("capability"));
    Assert.True(command.Has("online-only"));
    Assert.Equal(20, command.GetInt("page-size", 50));
  }

  [Fact]
  public void SchemaValidation_UsesDiscoveredSchema()
  {
    using var schemaDocument = JsonDocument.Parse(
      """{"type":"object","properties":{"cvText":{"type":"string"}},"required":["cvText"]}""");

    var valid = SchemaValidation.Validate(schemaDocument.RootElement, """{"cvText":"sample"}""");
    var invalid = SchemaValidation.Validate(schemaDocument.RootElement, """{"cvText":42}""");

    Assert.True(valid.IsValid);
    Assert.False(invalid.IsValid);
    Assert.NotEmpty(invalid.Errors);
  }
}

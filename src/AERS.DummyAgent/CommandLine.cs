namespace AERS.DummyAgent;

public sealed class CommandLine
{
  private static readonly HashSet<string> KnownOptions = new(StringComparer.OrdinalIgnoreCase)
  {
    "name", "domain", "capability", "query-domain", "input", "timeout", "parent-task-id",
    "api-key-env", "task-id", "no-connect", "expect-allowed", "page-size", "online-only", "count"
  };
  private readonly Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);
  private readonly List<string> positional = [];

  private CommandLine(string command) => Command = command;

  public string Command { get; }
  public IReadOnlyList<string> Positional => positional;

  public static CommandLine Parse(string[] args)
  {
    if (args.Length == 0)
      throw new ArgumentException("A command is required: serve, call, scenario, or inspect.");

    var command = args[0].ToLowerInvariant();
    var result = new CommandLine(command);
    for (var index = 1; index < args.Length; index++)
    {
      var argument = args[index];
      if (!argument.StartsWith("--", StringComparison.Ordinal))
      {
        if (result.Command == "scenario" && result.positional.Count == 0)
        {
          result.positional.Add(argument);
          continue;
        }
        throw new ArgumentException($"Unexpected argument '{argument}'.");
      }

      var name = argument[2..];
      if (name.Length == 0)
        throw new ArgumentException("Option names cannot be empty.");

      if (name.Equals("online-only", StringComparison.OrdinalIgnoreCase)
        || name.Equals("expect-timeout", StringComparison.OrdinalIgnoreCase)
        || name.Equals("disconnect-after-ack", StringComparison.OrdinalIgnoreCase)
        || name.Equals("answer-after-reconnect", StringComparison.OrdinalIgnoreCase))
      {
        result.options[name] = "true";
        continue;
      }
      if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException($"Option '--{name}' requires a value.");
      result.options[name] = args[index];
    }

    if (result.Get("input") is { } input)
    {
      try
      {
        using var document = System.Text.Json.JsonDocument.Parse(input);
      }
      catch (System.Text.Json.JsonException ex)
      {
        throw new ArgumentException($"'--input' must be valid JSON: {ex.Message}");
      }
    }
    return result;
  }

  public string? Get(string name) =>
    options.TryGetValue(name, out var value) ? value : null;

  public bool Has(string name) => options.ContainsKey(name);

  public int GetInt(string name, int defaultValue)
  {
    var value = Get(name);
    return value is null
      ? defaultValue
      : int.TryParse(value, out var parsed)
        ? parsed
        : throw new ArgumentException($"'--{name}' must be an integer.");
  }

  public double GetDouble(string name, double defaultValue)
  {
    var value = Get(name);
    return value is null
      ? defaultValue
      : double.TryParse(value, out var parsed)
        ? parsed
        : throw new ArgumentException($"'--{name}' must be a number.");
  }
}

namespace AERS.DummyAgent;

public sealed class CommandLine
{
  private readonly Dictionary<string, string?> options = new(StringComparer.OrdinalIgnoreCase);

  private CommandLine(string command)
  {
    Command = command;
  }

  public string Command { get; }

  public static CommandLine Parse(string[] args)
  {
    if (args.Length == 0)
      throw new ArgumentException("A command is required: provider, consumer, or seed.");

    var result = new CommandLine(args[0].ToLowerInvariant());
    for (var index = 1; index < args.Length; index++)
    {
      var argument = args[index];
      if (!argument.StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException($"Unexpected argument '{argument}'.");

      var name = argument[2..];
      if (name.Length == 0)
        throw new ArgumentException("Option names cannot be empty.");

      if (name.Equals("online-only", StringComparison.OrdinalIgnoreCase))
      {
        result.options[name] = "true";
        continue;
      }

      if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
        throw new ArgumentException($"Option '--{name}' requires a value.");
      result.options[name] = args[index];
    }

    return result;
  }

  public string? Get(string name) =>
    options.TryGetValue(name, out var value) ? value : null;

  public bool Has(string name) =>
    options.ContainsKey(name);

  public int GetInt(string name, int defaultValue)
  {
    var value = Get(name);
    return value is null
      ? defaultValue
      : int.TryParse(value, out var parsed) ? parsed : throw new ArgumentException($"'--{name}' must be an integer.");
  }
}

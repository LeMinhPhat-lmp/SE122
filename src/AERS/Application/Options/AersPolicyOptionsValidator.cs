using System.Text.RegularExpressions;

namespace Application.Options;

public static partial class AersPolicyOptionsValidator
{
  public static bool IsValid(AersPolicyOptions options)
  {
    if (options.DomainPolicy is null
      || !string.Equals(options.DomainPolicy.Mode, "DenyByDefault", StringComparison.Ordinal))
      return false;

    var domains = new HashSet<string>(StringComparer.Ordinal);
    foreach (var rule in options.DomainPolicy.CrossDomainRules ?? [])
    {
      var callerDomain = rule.CallerDomain?.Trim().ToLowerInvariant();
      var targetDomain = rule.TargetDomain?.Trim().ToLowerInvariant();
      if (callerDomain is null || targetDomain is null
        || !DomainPattern().IsMatch(callerDomain)
        || !DomainPattern().IsMatch(targetDomain)
        || !domains.Add($"{callerDomain}\u001f{targetDomain}"))
        return false;
    }

    return true;
  }

  [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
  private static partial Regex DomainPattern();
}

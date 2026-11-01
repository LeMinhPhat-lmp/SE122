using Application.Interfaces.Policies;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.Policies;

/// <summary>Decides whether one agent domain may access another agent domain.</summary>
public sealed class DomainAccessPolicy : IDomainAccessPolicy
{
  private readonly HashSet<DomainPair> crossDomainRules;

  public DomainAccessPolicy(IOptions<AersPolicyOptions> options)
  {
    ArgumentNullException.ThrowIfNull(options);

    var configuredRules = options.Value.DomainPolicy?.CrossDomainRules
      ?? throw new ArgumentException("Aers:Policy:DomainPolicy is required.", nameof(options));

    crossDomainRules = configuredRules
      .Select(rule => new DomainPair(Normalize(rule.CallerDomain), Normalize(rule.TargetDomain)))
      .ToHashSet();
  }

  /// <summary>
  /// Returns true for same-domain access or an explicitly configured directed rule.
  /// Further conditions, such as same-tenant access, should be added here.
  /// </summary>
  public bool CanAccess(string callerDomain, string targetDomain)
  {
    if (string.Equals(callerDomain, targetDomain, StringComparison.Ordinal))
      return true;

    if (crossDomainRules.Contains(new DomainPair(callerDomain, targetDomain)))
      return true;

    return false;
  }

  private static string Normalize(string domain) => domain.Trim().ToLowerInvariant();

  private readonly record struct DomainPair(string CallerDomain, string TargetDomain);
}

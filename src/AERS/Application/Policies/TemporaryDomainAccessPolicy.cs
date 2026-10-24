using Application.Interfaces.Policies;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Application.Policies;

/// <summary>
/// TEMPORARY implementation, used until the real DomainAccessPolicy from task B-01 is merged. It is the Week 4
/// inline rule extracted unchanged (same domain, or a directed pair on the allowlist). Task A-05 deletes this class
/// and registers the B-01 implementation.
/// </summary>
public sealed class TemporaryDomainAccessPolicy(IOptions<AersPolicyOptions> options) : IDomainAccessPolicy
{
  public bool CanAccess(string callerDomain, string targetDomain) =>
    string.Equals(callerDomain, targetDomain, StringComparison.OrdinalIgnoreCase)
    || options.Value.DomainPolicy.CrossDomainRules.Any(rule =>
        string.Equals(rule.CallerDomain, callerDomain, StringComparison.OrdinalIgnoreCase)
        && string.Equals(rule.TargetDomain, targetDomain, StringComparison.OrdinalIgnoreCase));
}

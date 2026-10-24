namespace Application.Interfaces.Policies;

/// <summary>The single place where cross-domain access is decided (decision A19).</summary>
public interface IDomainAccessPolicy
{
  /// <summary>
  /// True when <paramref name="callerDomain"/> equals <paramref name="targetDomain"/> or the directed pair
  /// (callerDomain, targetDomain) is listed in DomainPolicy.CrossDomainRules.
  /// </summary>
  bool CanAccess(string callerDomain, string targetDomain);
}

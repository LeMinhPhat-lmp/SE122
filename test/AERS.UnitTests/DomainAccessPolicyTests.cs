using Application.Extensions;
using Application.Interfaces.Policies;
using Application.Options;
using Application.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class DomainAccessPolicyTests
{
  [Fact]
  public void SameDomain_IsAllowed_AndUnlistedDifferentDomainIsDenied()
  {
    var policy = CreatePolicy();

    Assert.True(policy.CanAccess("hr", "hr"));
    Assert.False(policy.CanAccess("hr", "finance"));
  }

  [Fact]
  public void CrossDomainRules_AreDirected()
  {
    var policy = CreatePolicy(Rule("hr", "jobhunt"));

    Assert.True(policy.CanAccess("hr", "jobhunt"));
    Assert.False(policy.CanAccess("jobhunt", "hr"));
  }

  [Fact]
  public void SeveralRules_AreAllApplied()
  {
    var policy = CreatePolicy(Rule("hr", "jobhunt"), Rule("finance", "catalog"));

    Assert.True(policy.CanAccess("hr", "jobhunt"));
    Assert.True(policy.CanAccess("finance", "catalog"));
    Assert.False(policy.CanAccess("hr", "catalog"));
  }

  [Fact]
  public void Rules_NormalizeConfiguredCaseAndWhitespace()
  {
    var policy = CreatePolicy(Rule(" HR ", " JobHunt "));

    Assert.True(policy.CanAccess("hr", "jobhunt"));
  }

  [Fact]
  public void Policy_SnapshotsRulesAtConstruction()
  {
    var rules = new List<AersPolicyOptions.CrossDomainRule> { Rule("hr", "jobhunt") };
    var policy = CreatePolicy(rules.ToArray());
    rules.Clear();

    Assert.True(policy.CanAccess("hr", "jobhunt"));
  }

  [Fact]
  public void PolicyOptions_ValidateAtStartup()
  {
    Assert.False(IsValid("UnknownMode"));
    Assert.False(IsValid("DenyByDefault", Rule("hr!", "jobhunt")));
    Assert.False(IsValid("DenyByDefault", Rule("hr", "jobhunt"), Rule(" HR ", " JOBHUNT ")));
    Assert.True(IsValid("DenyByDefault", Rule(" HR ", " JobHunt ")));
  }

  [Fact]
  public void Policy_IsRegisteredAsSingleton()
  {
    var services = new ServiceCollection().AddApplicationServices();
    var descriptor = services.Single(service => service.ServiceType == typeof(IDomainAccessPolicy));

    Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    Assert.Equal(typeof(DomainAccessPolicy), descriptor.ImplementationType);
  }

  private static DomainAccessPolicy CreatePolicy(
    params AersPolicyOptions.CrossDomainRule[] rules) =>
    new(Options.Create(new AersPolicyOptions
    {
      DomainPolicy = new() { CrossDomainRules = rules.ToList() }
    }));

  private static bool IsValid(
    string mode, params AersPolicyOptions.CrossDomainRule[] rules) =>
    AersPolicyOptionsValidator.IsValid(new AersPolicyOptions
    {
      DomainPolicy = new() { Mode = mode, CrossDomainRules = rules.ToList() }
    });

  private static AersPolicyOptions.CrossDomainRule Rule(string caller, string target) =>
    new() { CallerDomain = caller, TargetDomain = target };
}

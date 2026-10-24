using Application.Constants;
using Application.DTOs.Result.Enums;
using Xunit;

namespace AERS.UnitTests;

public sealed class AgentRegistryServiceCapabilitiesTests
{
  private readonly FakeAgentRepository repository = new();
  private readonly FakePresenceStore presence = new();

  private Application.Services.AgentRegistryService CreateService(FakeDomainAccessPolicy policy) =>
    TestData.CreateService(repository, presence, policy);

  private static FakeDomainAccessPolicy AllowSameDomainOnly() => new((caller, target) => caller == target);

  [Fact]
  public async Task Same_domain_caller_gets_capabilities_ordered_by_name()
  {
    var target = TestData.CreateAgent("jobhunt", 0, "zeta", "alpha", "Beta");
    repository.Agents.Add(target);

    var result = await CreateService(AllowSameDomainOnly()).GetCapabilitiesAsync(target.Id, TestData.AgentCaller(Guid.NewGuid(), "jobhunt"));

    Assert.True(result.IsSuccess);
    Assert.Equal(target.Id, result.Data!.AgentId);
    Assert.Equal(["Beta", "alpha", "zeta"], result.Data.Capabilities.Select(capability => capability.Name).ToArray());
  }

  [Fact]
  public async Task Agent_can_read_its_own_capabilities_without_the_policy()
  {
    var target = TestData.CreateAgent("jobhunt", 0, "alpha");
    repository.Agents.Add(target);
    var policy = new FakeDomainAccessPolicy((_, _) => false);

    var result = await CreateService(policy).GetCapabilitiesAsync(target.Id, TestData.AgentCaller(target.Id, "jobhunt"));

    Assert.True(result.IsSuccess);
    Assert.Equal(0, policy.Calls);
  }

  [Fact]
  public async Task Missing_agent_is_not_found_even_when_the_policy_would_deny()
  {
    var result = await CreateService(new FakeDomainAccessPolicy((_, _) => false))
      .GetCapabilitiesAsync(Guid.NewGuid(), TestData.AgentCaller(Guid.NewGuid(), "jobhunt"));

    Assert.Equal(ResultStatus.NotFound, result.ResultStatus);
    Assert.Equal(ErrorCodes.AgentNotFound, result.ErrorCode);
  }

  [Fact]
  public async Task Deregistered_agent_is_not_found()
  {
    var target = TestData.CreateAgent("jobhunt", 0, "alpha");
    target.DeletedAt = TestData.BaseTime;
    repository.Agents.Add(target);

    var result = await CreateService(AllowSameDomainOnly()).GetCapabilitiesAsync(target.Id, TestData.AgentCaller(Guid.NewGuid(), "jobhunt"));

    Assert.Equal(ResultStatus.NotFound, result.ResultStatus);
  }

  [Fact]
  public async Task Cross_domain_caller_denied_by_policy_is_forbidden()
  {
    var target = TestData.CreateAgent("jobhunt", 0, "alpha");
    repository.Agents.Add(target);

    var result = await CreateService(AllowSameDomainOnly()).GetCapabilitiesAsync(target.Id, TestData.AgentCaller(Guid.NewGuid(), "hr"));

    Assert.Equal(ResultStatus.Forbidden, result.ResultStatus);
    Assert.Equal(ErrorCodes.DomainForbidden, result.ErrorCode);
  }

  [Fact]
  public async Task Cross_domain_caller_allowed_by_policy_succeeds()
  {
    var target = TestData.CreateAgent("jobhunt", 0, "alpha");
    repository.Agents.Add(target);

    var result = await CreateService(new FakeDomainAccessPolicy((_, _) => true)).GetCapabilitiesAsync(target.Id, TestData.AgentCaller(Guid.NewGuid(), "hr"));

    Assert.True(result.IsSuccess);
  }
}

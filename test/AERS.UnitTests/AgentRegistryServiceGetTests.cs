using Application.Constants;
using Application.DTOs.Result.Enums;
using Xunit;

namespace AERS.UnitTests;

// Guards decision A2 / spec 1.8 after GetAsync was moved onto IDomainAccessPolicy (404 first, then access).
public sealed class AgentRegistryServiceGetTests
{
  private readonly FakeAgentRepository repository = new();

  [Fact]
  public async Task Unknown_agent_is_not_found_before_any_access_check()
  {
    var policy = new FakeDomainAccessPolicy((_, _) => false);
    var service = TestData.CreateService(repository, new FakePresenceStore(), policy);

    var result = await service.GetAsync(Guid.NewGuid(), TestData.AgentCaller(Guid.NewGuid(), "jobhunt"));

    Assert.Equal(ResultStatus.NotFound, result.ResultStatus);
    Assert.Equal(0, policy.Calls);
  }

  [Fact]
  public async Task Cross_domain_caller_denied_by_policy_is_forbidden()
  {
    var target = TestData.CreateAgent("jobhunt", 0, "alpha");
    repository.Agents.Add(target);
    var service = TestData.CreateService(repository, new FakePresenceStore(), new FakeDomainAccessPolicy((_, _) => false));

    var result = await service.GetAsync(target.Id, TestData.AgentCaller(Guid.NewGuid(), "hr"));

    Assert.Equal(ResultStatus.Forbidden, result.ResultStatus);
    Assert.Equal(ErrorCodes.DomainForbidden, result.ErrorCode);
  }
}

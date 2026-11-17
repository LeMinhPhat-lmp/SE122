using Application.DTOs.Agent.Requests;
using Application.Options;
using Application.Services;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class DeregistrationTerminationTests
{
  [Fact]
  public async Task Deregistration_calls_the_terminator_once_after_the_commit()
  {
    var agents = new InMemoryAgentRepository();
    var agent = TestData.CreateAgent("jobhunt", 0, "echo");
    agents.Agents.Add(agent);
    var terminator = new NoopConnectionTerminator();
    var presence = new RecordingPresenceStore();
    var service = new AgentRegistryService(
      new InMemoryUnitOfWork(agents, new InMemoryTaskRepository(), new InMemoryAuditRepository(), new InMemoryCredentialRepository()),
      new InlineValidator<AgentRegistrationRequest>(),
      new UnusedApiKeyService(),
      presence,
      Microsoft.Extensions.Options.Options.Create(new AersPolicyOptions()),
      Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions()),
      new FakeDomainAccessPolicy((caller, target) => caller == target),
      terminator,
      TimeProvider.System,
      NullLogger<AgentRegistryService>.Instance);

    var result = await service.DeregisterAsync(agent.Id, TestData.AgentCaller(agent.Id, "jobhunt"));

    Assert.True(result.IsSuccess);
    Assert.True(agent.IsDeleted);
    Assert.Equal(agent.Id, Assert.Single(terminator.Terminated));
    Assert.Equal(agent.Id, Assert.Single(presence.Removed));
  }

  [Fact]
  public async Task Rejected_deregistration_does_not_terminate_anything()
  {
    var agents = new InMemoryAgentRepository();
    var agent = TestData.CreateAgent("jobhunt", 0, "echo");
    agents.Agents.Add(agent);
    var terminator = new NoopConnectionTerminator();
    var service = new AgentRegistryService(
      new InMemoryUnitOfWork(agents, new InMemoryTaskRepository(), new InMemoryAuditRepository(), new InMemoryCredentialRepository()),
      new InlineValidator<AgentRegistrationRequest>(), new UnusedApiKeyService(), new RecordingPresenceStore(),
      Microsoft.Extensions.Options.Options.Create(new AersPolicyOptions()),
      Microsoft.Extensions.Options.Options.Create(new DiscoveryOptions()),
      new FakeDomainAccessPolicy((caller, target) => caller == target), terminator, TimeProvider.System,
      NullLogger<AgentRegistryService>.Instance);

    var result = await service.DeregisterAsync(agent.Id, TestData.AgentCaller(Guid.NewGuid(), "jobhunt"));

    Assert.False(result.IsSuccess);
    Assert.Empty(terminator.Terminated);
  }
}

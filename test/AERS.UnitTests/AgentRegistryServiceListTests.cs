using Application.Constants;
using Application.DTOs.Agent.Requests;
using Application.DTOs.Result.Enums;
using Application.Discovery;
using Application.Exceptions;
using Application.Interfaces.Repositories;
using Application.Options;
using Xunit;

namespace AERS.UnitTests;

public sealed class AgentRegistryServiceListTests
{
  private readonly FakeAgentRepository repository = new();
  private readonly FakePresenceStore presence = new();
  private readonly FakeDomainAccessPolicy policy = new((caller, target) =>
    caller == target || (caller == "hr" && target == "jobhunt"));

  private readonly Guid callerId = Guid.NewGuid();

  private Application.Services.AgentRegistryService CreateService(DiscoveryOptions? options = null) =>
    TestData.CreateService(repository, presence, policy, options);

  private static AgentQuery Query(string? domain = null, string? capability = null, bool onlineOnly = false, int? limit = null, string? cursor = null) =>
    new(domain, capability, onlineOnly, limit, cursor);

  [Fact]
  public async Task Registration_key_caller_is_rejected_with_authentication_failed()
  {
    var result = await CreateService().ListAsync(Query(), TestData.RegistrationKeyCaller());

    Assert.Equal(ResultStatus.Unauthorized, result.ResultStatus);
    Assert.Equal(ErrorCodes.AuthenticationFailed, result.ErrorCode);
    Assert.Equal(0, repository.ListCalls);
  }

  [Fact]
  public async Task Without_domain_the_callers_own_domain_is_listed()
  {
    var result = await CreateService().ListAsync(Query(), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.True(result.IsSuccess);
    Assert.Equal("jobhunt", repository.LastListArguments!.Value.Domain);
    Assert.Equal(50, repository.LastListArguments.Value.Limit);
  }

  [Fact]
  public async Task Explicit_own_domain_is_allowed_and_normalized()
  {
    var result = await CreateService().ListAsync(Query(domain: "  JobHunt "), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.True(result.IsSuccess);
    Assert.Equal("jobhunt", repository.LastListArguments!.Value.Domain);
  }

  [Fact]
  public async Task Other_domain_denied_by_policy_returns_forbidden_without_touching_the_repository()
  {
    var result = await CreateService().ListAsync(Query(domain: "hr"), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(ResultStatus.Forbidden, result.ResultStatus);
    Assert.Equal(ErrorCodes.DomainForbidden, result.ErrorCode);
    Assert.Equal(0, repository.ListCalls);
  }

  [Fact]
  public async Task Other_domain_allowed_by_policy_is_listed()
  {
    var result = await CreateService().ListAsync(Query(domain: "jobhunt"), TestData.AgentCaller(callerId, "hr"));

    Assert.True(result.IsSuccess);
    Assert.Equal("jobhunt", repository.LastListArguments!.Value.Domain);
  }

  [Fact]
  public async Task Invalid_values_are_all_reported_together_with_their_paths()
  {
    var result = await CreateService().ListAsync(
      Query(domain: "Not Valid!", capability: "1bad", limit: 0, cursor: "garbage"),
      TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(ResultStatus.ValidationError, result.ResultStatus);
    Assert.Equal(ErrorCodes.ValidationError, result.ErrorCode);
    Assert.Equal(["/domain", "/capability", "/limit", "/cursor"], result.ErrorDetails.Select(detail => detail.Path).ToArray());
    Assert.Equal(0, policy.Calls);
    Assert.Equal(0, repository.ListCalls);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(101)]
  [InlineData(-5)]
  public async Task Limit_outside_the_allowed_range_is_rejected(int limit)
  {
    var result = await CreateService().ListAsync(Query(limit: limit), TestData.AgentCaller(callerId, "jobhunt"));

    var detail = Assert.Single(result.ErrorDetails);
    Assert.Equal("/limit", detail.Path);
    Assert.Equal("limit must be between 1 and 100.", detail.Message);
  }

  [Fact]
  public async Task Validation_is_evaluated_before_domain_access()
  {
    // domain "hr" is forbidden for a jobhunt caller, but the invalid limit must win (400 before 403).
    var result = await CreateService().ListAsync(Query(domain: "hr", limit: 1000), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(ResultStatus.ValidationError, result.ResultStatus);
  }

  [Fact]
  public async Task Capability_filter_is_passed_through_without_case_conversion()
  {
    await CreateService().ListAsync(Query(capability: "Analyze_CV"), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal("Analyze_CV", repository.LastListArguments!.Value.Capability);
  }

  [Fact]
  public async Task Configured_page_sizes_are_used()
  {
    var service = CreateService(new DiscoveryOptions { DefaultPageSize = 10, MaxPageSize = 20 });

    await service.ListAsync(Query(), TestData.AgentCaller(callerId, "jobhunt"));
    var tooLarge = await service.ListAsync(Query(limit: 21), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(10, repository.LastListArguments!.Value.Limit);
    Assert.Equal("limit must be between 1 and 20.", Assert.Single(tooLarge.ErrorDetails).Message);
  }

  [Fact]
  public async Task Valid_cursor_is_decoded_and_forwarded()
  {
    var position = new DiscoveryCursorPosition(TestData.BaseTime, Guid.NewGuid());

    await CreateService().ListAsync(Query(cursor: DiscoveryCursor.Encode(position)), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(position, repository.LastListArguments!.Value.After);
  }

  [Fact]
  public async Task Online_only_false_never_calls_presence()
  {
    repository.PageToReturn = new([TestData.CreateAgent("jobhunt", 0, "a")], false);

    var result = await CreateService().ListAsync(Query(onlineOnly: false), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(0, presence.BatchCalls);
    Assert.Single(result.Data!.Agents);
  }

  [Fact]
  public async Task Online_only_true_keeps_only_online_agents_and_cursor_comes_from_the_last_scanned_row()
  {
    var first = TestData.CreateAgent("jobhunt", 0, "a");
    var second = TestData.CreateAgent("jobhunt", 1, "a");
    var third = TestData.CreateAgent("jobhunt", 2, "a");
    repository.PageToReturn = new([first, second, third], true);
    presence.Online.Add(first.Id); // the last scanned row (third) is offline on purpose

    var result = await CreateService().ListAsync(Query(onlineOnly: true), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(1, presence.BatchCalls);
    Assert.Equal([first.Id], result.Data!.Agents.Select(agent => agent.AgentId).ToArray());
    Assert.NotNull(result.Data.NextCursor);
    Assert.True(DiscoveryCursor.TryDecode(result.Data.NextCursor!, out var next));
    Assert.Equal(new DiscoveryCursorPosition(third.CreatedAt, third.Id), next);
  }

  [Fact]
  public async Task Online_only_can_return_an_empty_page_with_a_next_cursor()
  {
    repository.PageToReturn = new([TestData.CreateAgent("jobhunt", 0, "a")], true);

    var result = await CreateService().ListAsync(Query(onlineOnly: true), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Empty(result.Data!.Agents);
    Assert.NotNull(result.Data.NextCursor);
  }

  [Fact]
  public async Task No_further_rows_means_a_null_cursor()
  {
    repository.PageToReturn = new([TestData.CreateAgent("jobhunt", 0, "a")], false);

    var result = await CreateService().ListAsync(Query(), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Null(result.Data!.NextCursor);
  }

  [Fact]
  public async Task Empty_result_is_a_success_with_no_agents()
  {
    var result = await CreateService().ListAsync(Query(onlineOnly: true), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.True(result.IsSuccess);
    Assert.Empty(result.Data!.Agents);
    Assert.Null(result.Data.NextCursor);
    Assert.Equal(0, presence.BatchCalls);
  }

  [Fact]
  public async Task Redis_failure_with_online_only_propagates_to_the_global_handler()
  {
    repository.PageToReturn = new([TestData.CreateAgent("jobhunt", 0, "a")], false);
    presence.FailWith = new StateStoreUnavailableException(new InvalidOperationException("redis down"));

    await Assert.ThrowsAsync<StateStoreUnavailableException>(() =>
      CreateService().ListAsync(Query(onlineOnly: true), TestData.AgentCaller(callerId, "jobhunt")));
  }

  [Fact]
  public async Task Cards_list_capabilities_ordered_by_name_ordinal()
  {
    repository.PageToReturn = new([TestData.CreateAgent("jobhunt", 0, "b_cap", "B_cap", "a_cap")], false);

    var result = await CreateService().ListAsync(Query(), TestData.AgentCaller(callerId, "jobhunt"));

    Assert.Equal(["B_cap", "a_cap", "b_cap"], result.Data!.Agents[0].Capabilities.Select(capability => capability.Name).ToArray());
  }
}

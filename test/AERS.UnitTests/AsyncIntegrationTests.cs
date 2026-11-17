using System.Net;
using System.Text.Json;
using Xunit;

namespace AERS.UnitTests;

/// <summary>
/// B-05 test matrix. The runtime scenarios are intentionally kept as explicit
/// skipped tests until their A-04 production prerequisites are available.
/// This prevents a green suite from silently claiming coverage that the
/// current server does not implement.
/// </summary>
[Collection("Discovery integration")]
public sealed class AsyncIntegrationTests(DiscoveryIntegrationFixture fixture)
{
  [SkippableFact] public Task IT_W7_01_WorkingStatusEvent() => NotReady("SignalR async flow harness and A41 status push are not available.");
  [SkippableFact] public Task IT_W7_02_AsyncCompletionAfterWorking() => NotReady("SignalR async flow harness is not available.");
  [SkippableFact] public Task IT_W7_03_NoStatusEventForTerminalState() => NotReady("SignalR async flow harness is not available.");
  [SkippableFact] public Task IT_W7_04_UnavailableTargetFailsWithoutWorkingEvent() => NotReady("Requires the B-05 SignalR flow harness.");
  [SkippableFact] public Task IT_W7_05_CallerDisconnectDoesNotCancelTask() => NotReady("Requires the B-05 SignalR flow harness.");

  [SkippableFact] public Task IT_W7_06_RuntimeStateHashAfterCreation() => NotReady("ITaskRuntimeStateStore is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_07_RuntimeStateTtlTracksDeadline() => NotReady("ITaskRuntimeStateStore is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_08_RuntimeStateAfterCompletion() => NotReady("ITaskRuntimeStateStore is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_09_RedisOutagePreservesOutcome() => NotReady("Redis runtime-state integration is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_10_RuntimeStateAlwaysHasTtl() => NotReady("ITaskRuntimeStateStore is not implemented in the current production baseline.");

  [SkippableFact] public Task IT_W7_11_SilentTargetTimesOut() => NotReady("A-02 timeout worker is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_12_TimeoutResultIsPushed() => NotReady("A-02 timeout worker is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_13_GetTaskAfterTimeout() => NotReady("A-02 timeout worker is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_14_LateResultAfterTimeoutIsRejected() => NotReady("A-02 timeout worker is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_15_CompletedTaskIsNotExpired() => NotReady("A-02 timeout worker is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_16_SubmittedTaskCanExpire() => NotReady("A-02 timeout worker is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_17_ChildDeadlineIsBoundByParent() => NotReady("A-02 child-deadline production rule is not wired into TaskService.");
  [SkippableFact] public Task IT_W7_18_RootDeadlineUsesDefaultTimeout() => NotReady("A-02 timeout worker integration is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_19_TwoHostsExpireTasksExactlyOnce() => NotReady("A-02 timeout worker integration is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_20_WorkerSurvivesRepositoryFailure() => NotReady("A-02 timeout worker integration is not implemented in the current production baseline.");

  [SkippableFact] public Task IT_W7_21_OfflineCallerResultIsRecoverable() => NotReady("ADR-W7-02 acknowledged_at recovery is not implemented.");
  [SkippableFact] public Task IT_W7_22_ReconnectRedeliversAndAcknowledges() => NotReady("ADR-W7-02 acknowledged_at recovery is not implemented.");
  [SkippableFact] public Task IT_W7_23_OnlineCallerReceivesOnce() => NotReady("ADR-W7-02 acknowledged_at recovery is not implemented.");
  [SkippableFact] public Task IT_W7_24_OfflineTimeoutIsRedelivered() => NotReady("ADR-W7-02 acknowledged_at recovery is not implemented.");
  [SkippableFact] public Task IT_W7_25_WorkingTaskAckIsRejected() => NotReady("task.ack is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_26_CrossCallerAckIsRejected() => NotReady("task.ack is not implemented in the current production baseline.");
  [SkippableFact] public Task IT_W7_27_RedeliveryBatchIsBounded() => NotReady("ADR-W7-02 acknowledged_at recovery is not implemented.");
  [SkippableFact] public Task IT_W7_28_TargetReconnectKeepsTaskAccepted() => NotReady("ADR-W7-03 disconnect tracking is not implemented.");
  [SkippableFact] public Task IT_W7_29_TargetDisconnectUsesGracePeriod() => NotReady("ADR-W7-03 disconnect grace worker is not implemented.");

  [SkippableFact]
  public async Task IT_W7_30_Week6GetTaskNotFoundPrecedesForbidden()
  {
    IntegrationTestSkip.Require();
    var client = fixture.CreateClient();
    var caller = await fixture.RegisterAgentAsync(client, UniqueDomain(), "Caller", Capability("echo"));
    var missingTaskId = Guid.NewGuid();

    using var request = new HttpRequestMessage(HttpMethod.Get, $"tasks/{missingTaskId}");
    request.Headers.Add("X-Api-Key", caller.ApiKey);
    using var response = await client.SendAsync(request);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal("TASK_NOT_FOUND", json.RootElement.GetProperty("error").GetProperty("code").GetString());
  }

  private static Task NotReady(string reason)
  {
    Xunit.Skip.If(true, reason);
    return Task.CompletedTask;
  }

  private static object Capability(string name) => new
  {
    name,
    description = "Integration capability.",
    inputSchema = new { type = "object" },
    outputSchema = new { type = "object" }
  };

  private static string UniqueDomain() => $"it{Guid.NewGuid():N}"[..20];
}

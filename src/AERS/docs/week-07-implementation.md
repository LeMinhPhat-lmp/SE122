# Week 7 implementation notes

## B-01 rate limiter

`IRateLimiter` and `RateLimitDecision` follow the frozen Week 7 contract. The
Infrastructure implementation is `RedisRateLimiter`, a scoped Redis store that
uses one Lua script for `INCR` and TTL assignment. The key is:

```text
{KeyPrefix}ratelimit:{callerAgentId}:{windowStartUnixSeconds}
```

The window start is calculated from the injected `TimeProvider`. The script
assigns `WindowSeconds + 1` seconds of TTL atomically when the counter is
created (and repairs a pre-existing key without TTL). Denied requests still
increment the counter and `Remaining` is clamped to zero.

Redis failures are translated by `RedisStoreBase` into
`StateStoreUnavailableException`; the limiter does not select the request
failure mode. ADR-W8-01 is currently **PROVISIONAL** because its repository
copy is `Proposed`; the requested owner choice is the suggested default:
early task-create limiting, fail-closed in the caller, and no REST limiting.

`Aers:Policy:MaxRecursionDepth` and
`Aers:Policy:RateLimit:{WindowSeconds,MaxTasksPerWindow}` are bound in the
existing policy options and validated on start. Defaults are 5, 60, and 100.

Validation:

```bash
mise exec dotnet -- dotnet build
mise exec dotnet -- dotnet test test/AERS.UnitTests/AERS.UnitTests.csproj --no-restore
```

The Redis concurrency/TTL test remains an integration concern for the Week 7
integration task; the local unit suite validates options and existing policy
behavior without requiring a running Redis container.

## B-02 recursion policy

`TaskPolicyEvaluator.EvaluateRecursion` is synchronous and pure. It checks
depth before cycle membership:

| Task | Caller → target | `ancestorChain` | Result |
|---|---|---|---|
| T1 | A → B | `[A]` | allowed |
| T2 | B → C | `[A, B]` | allowed |
| T3 | C → A | `[A, B, C]` | `CYCLE_DETECTED` |
| T3 | C → B | `[A, B, C]` | `CYCLE_DETECTED` |
| root | A → A | `[A]` | `CYCLE_DETECTED` |
| branch | C → D | `[A, B, C]` | allowed |

With `MaxRecursionDepth = 5`, depths 0 through 5 are allowed and depth 6 is
rejected with `DEPTH_EXCEEDED`. The evaluator does not access repositories,
Redis, a registry, a limiter, or a clock. A-06 is responsible for inserting
this stage into `TaskService.CreateAsync`.

Cross-domain access remains **PROVISIONAL (ADR-W8-05)** and uses the existing
allowlist-only `DenyByDefault` policy. Same-domain calls are allowed,
configured ordered pairs such as `hr → jobhunt` are allowed after
normalization, and reverse or unknown pairs are denied. No Agent Card field or
new configuration was added.

## B-03 asynchronous AgentClient

`AersAgentConnection` provides asynchronous `SubmitAsync`, `TaskHandle`,
working notifications, first-result-wins completion, `WaitAsync`, polling
fallback through `ObserveAsync`, and `GetTaskAsync`. SignalR handlers are
registered once by `SignalRHubTransport`; automatic reconnect uses bounded
backoff and pending handles remain usable after reconnect. Invocation handlers
are dispatched outside the receive callback so nested submissions do not block
the SignalR receive loop. Duplicate terminal results are ignored and retained
entries are pruned after the configured retention period.

`FileCredentialStore` is **PROVISIONAL (ADR-W9-03)** because the ADR file was
not present in the repository. It stores `{agentId, apiKey}` atomically in a
per-agent JSON file with user-only permissions where supported.
`AersClient.RegisterOrResumeAsync` reuses a stored credential and registers
again only after a missing or unauthorized credential; failed registration
never overwrites the existing file. `.aers-agents/` is intentionally local
runtime state and must not be committed.

## B-04 Dummy Agent scenarios

`AERS.DummyAgent.Core` is a server-independent library containing deterministic
`slow`, `silent`, `drop-after-receive`, and `chain` behaviors in addition to
the Week 6 behaviors. Slow work is awaited by the client invocation task and
does not block the SignalR receive callback; silent returns
`InvocationResult.SuppressReply`; drop/reconnect uses the client connection
stop/start hooks. `ScenarioRunner` validates supported scenario names and
provides a dry-run result; full persistence, timeout, Redis, and audit
assertions belong to the B-05 integration pass.

ADR-W7-03, ADR-W8-01, ADR-W8-02, and ADR-W8-03 were not present as Accepted
ADRs in the repository. Their suggested defaults are therefore marked
**PROVISIONAL** and documented in [week-07-demo.md](week-07-demo.md).

## B-05 async integration test matrix

`test/AERS.UnitTests/AsyncIntegrationTests.cs` preserves IDs IT-W7-01 through
IT-W7-30. IT-W7-30 reruns the available Week 6 `404 before 403` regression.
The remaining tests are explicit skips, not false-positive placeholders:
the current production baseline does not yet provide the A-02 timeout worker,
Redis runtime-state store, ADR-W7-02 acknowledged-result recovery,
ADR-W7-03 disconnect grace worker, or the real SignalR integration helper.

The four required ADRs were still `Proposed`; this pass uses their suggested
defaults provisionally, as authorized by the owner. The skipped tests should
be enabled once the corresponding A-02/A-03/A-04 prerequisites are delivered.

## B-06 security integration test matrix

`test/AERS.UnitTests/SecurityIntegrationTests.cs` preserves IT-W8-01 through
IT-W8-30. The matrix uses the requested provisional defaults for the missing
ADR-W8-01 through ADR-W8-05 decisions, but every case is currently an explicit
skip because the A-05/A-06 production integration points and the Week 8
recording hub harness are not present. No security behavior is claimed by
these skipped cases; they are activation points for the next implementation
pass.
# Week 7 implementation notes (member A)

Scope: A-01 to A-09 of `week-07/A`, with the nine ADRs in `.ai/week-07/ADR-decisions` treated as **Accepted**
(W7-01, W7-02, W7-03, W7-04, W8-01, W8-02, W8-03, W8-04, W9-02).

## What was built

| Task | Result |
|---|---|
| A-01 Runtime state | `ITaskRuntimeStateStore` / `RedisTaskRuntimeStateStore`. One Lua script does `HSET` + `PEXPIRE`, so the hash never exists without a TTL. `TaskService` writes it after every commit that won its compare-and-set (create, dispatch, result, expiry). Write-only: nothing reads it (ADR-W7-01). A Redis failure is a warning with the task id. |
| A-02 Timeout | `TaskService.ExpireAsync`, `ITaskRepository.ListExpiredAsync`, `API/Workers/TaskTimeoutWorker` (every instance runs it; the compare-and-set in PostgreSQL decides). Child deadline = `min(now + timeout, parent.deadline)` (ADR-W7-04). |
| A-03 Notifier | `TaskNotifier` is the only component that pushes `task.result` / `task.status_event`. It never throws and stores nothing (ADR-W7-02). `TaskMessageService` pushes `working` after a successful dispatch. |
| A-04 Redelivery | **No Redis undelivered set.** `tasks.acknowledged_at` + client message `task.ack`. On connect the hub starts `TaskRecoveryService.RedeliverAsync` in the background: terminal tasks of the caller with `acknowledged_at IS NULL`, up to `RedeliveryBatchSize`. At least once; clients dedupe by `taskId`. |
| A-05 Disconnect | Hub `OnDisconnectedAsync` records the disconnect in the Redis sorted set `{prefix}disconnected` only when `UnbindAsync` really removed the mapping. After `TargetDisconnectGraceSeconds` (30) the worker fails the target's working tasks with `TARGET_UNAVAILABLE`, unless the target reconnected (ADR-W7-03). |
| A-06 Policy | Order: authenticate, shape, caller active, **rate limit**, target, parent, scope, input, depth, cycle, create (ADR-W8-01). `RATE_LIMITED`, `DEPTH_EXCEEDED`, `CYCLE_DETECTED` carry the A57 `details` and `retryable`. Limiter outage is fail-closed (`INTERNAL_ERROR`, retryable). Orphan hint in the audit event (ADR-W8-02). Output schema check is warning-only (ADR-W9-02). |
| A-07 Observability | Information log per expiry / disconnect failure / redelivery summary; warnings for every best-effort failure. No payload, input, output or credential is logged. |
| A-08 HubProbe | `watch`, `ack`, `--auto-ack`, `--repeat`, `--wait 0`, behaviors `silent`, `reconnect`, `orphan-root`. |
| A-09 Docs | `docs/design/*` (see below). |

Revocation (ADR-W8-03): `AgentRegistryService.DeregisterAsync` publishes the agent id on Redis channel `{prefix}agent:revoked`
(`RedisConnectionTerminator`). Every instance runs `ConnectionTerminationSubscriber`, which aborts the connection through
`LiveConnectionTracker`. Second line of defense: `task.create`, `task.result`, `task.status_event` and `task.ack` all re-check that the
sender is an active agent. Authentication failures are logged with a limiter (ADR-W8-04): at most one log per
(remote address, reason) per 10 seconds, 10,000 entries max, never the credential.

## Things that go beyond the A tasks (read before merging)

1. **B-01 and B-02 parts are included.** A-06 cannot work without `IRateLimiter` and `ITaskPolicyEvaluator.EvaluateRecursion`, and they
   do not exist in the source. `RedisRateLimiter`, `EvaluateRecursion`, `MaxRecursionDepth` and `RateLimit` options are therefore
   delivered here, written to the frozen contract. If B already has versions, keep B's and delete mine; the signatures are identical.
2. **`IConnectionRegistry.UnbindAsync` now returns `Task<bool>`** (it was `Task`). ADR-W7-03 needs to know whether the mapping was removed.
   Existing call sites compile unchanged; only a hand-written fake that implements the interface needs `Task<bool>`.
3. **No EF migration is included.** The repository has only `FirstMigration` (no `tasks` tables) and I cannot run `dotnet ef` here.
   A hand-written snapshot would make `Migrate()` fail with a pending-model-changes error. Run, from `src/AERS`:
   `dotnet ef migrations add AddTasks -p Infrastructure -s API` (if you have no tasks migration yet; it includes `acknowledged_at` and the
   partial index `ix_tasks_caller_unacknowledged`), or `dotnet ef migrations add AddTaskAcknowledgedAt -p Infrastructure -s API`
   if you already generated the tasks migration locally.
4. **Nothing was compiled or run.** There is no .NET SDK in my environment. Run `dotnet build` and `dotnet test` first.
5. ADR-W8-05, W9-01 and W9-03 were not in the ADR folder; they only affect B's tasks.

## Design notes

* `TaskService` is the only place that changes a task. Every transition is `TransitionAsync` (compare-and-set) followed by a best-effort
  runtime state write. Workers and the hub never write the database directly.
* Rate limiting happens before the target lookup. A request rejected later (unknown target, scope, depth, cycle) has already used one unit of
  quota; shape errors and unknown/deregistered callers use none.
* The worker repeats the expiry query while a full batch comes back, but stops when a full batch produced no success, so a batch that
  keeps failing cannot spin.
* Redis sorted set `{prefix}disconnected` has no TTL. A stale mark is removed by the worker when the agent is connected again or has no
  working task left.
* New options (all in `Aers:Tasks`): `TimeoutScanIntervalSeconds` 5, `TimeoutBatchSize` 100, `RuntimeStateGraceSeconds` 60,
  `RuntimeStateTerminalTtlSeconds` 60, `RedeliveryBatchSize` 100, `TargetDisconnectGraceSeconds` 30. In `Aers:Policy`:
  `MaxRecursionDepth` 5, `RateLimit:WindowSeconds` 60, `RateLimit:MaxTasksPerWindow` 100.

## Files

New: `Application/Features/{TaskNotifier,TaskRecoveryService}.cs`, `Application/Interfaces/...` (ITaskRuntimeStateStore, IDisconnectTracker,
IConnectionTerminator, IRevocationSubscriber, IRateLimiter, ITaskNotifier, ITaskRecoveryService), `Application/Models/{TaskRuntimeState,RateLimitDecision}.cs`,
`Application/DTOs/Tasks/{TaskAck,TaskStatusEventPush}.cs`, `Infrastructure/Runtime/Redis*.cs` (five new classes), `API/Workers/TaskTimeoutWorker.cs`,
`API/Realtime/{LiveConnectionTracker,ConnectionTerminationSubscriber}.cs`, `API/Authentication/AuthFailureLogLimiter.cs`, unit tests, docs.

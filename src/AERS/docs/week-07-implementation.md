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

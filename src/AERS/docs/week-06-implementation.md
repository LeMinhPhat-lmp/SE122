# Week 6 Implementation Guide: Task Model + SignalR Relay (server side, member A)

Source of truth: `Protocol-Specification-v3.md` plus the Week 6 contract (decisions A20-A40). Tasks B-02 to B-05 (client library,
Dummy Agent, integration tests, spec sync) add their own sections; they are not repeated here.

## 1. Overview

Week 6 makes the synchronous Agent-to-Agent call A -> B work end to end through AERS.

| Entry point | Credential | Purpose |
|---|---|---|
| Hub `/hubs/agent`, method `Send(TaskEnvelope) -> EnvelopeAck` | `X-Api-Key` (negotiate + transport) | `task.create`, `task.result`, `task.status_event` from agents |
| Hub client method `Receive(TaskEnvelope)` | n/a (server to client) | `task.invoke` to the target, `task.result` to the caller |
| `GET /tasks/{taskId}` | `X-Api-Key` | Read state/result from PostgreSQL (caller or target only) |

## 2. Synchronous flow

```text
A  --Send(task.create)--------------------------------> AERS
AERS: shape check -> caller active -> target exists -> parent rules -> policy -> INSERT task (submitted) + audit task.created
AERS: resolve B's connection
        none  -> submitted -> failed (TARGET_UNAVAILABLE), no send, push failed task.result to A, ack(failed)
AERS: COMMIT submitted -> working (history + audit)          <- committed BEFORE sending
AERS --Receive(task.invoke)--> B     (send throws -> working -> failed TARGET_UNAVAILABLE)
A  <-- ack { accepted:true, taskId, status:"working" }
B  --Send(task.result)--------------------------------> AERS
AERS: sender is target? task exists and is working? payload rules? CAS working -> completed|failed (history + audit)
B  <-- ack { accepted:true, taskId, status }
AERS --Receive(task.result)--> A     (best effort; A offline -> nothing lost, GET /tasks/{id} serves it)
```

The ack to `task.create` and the pushed `task.result` travel on the same connection and their relative order is not guaranteed: clients
must buffer results by `taskId`.

| From | To | When | Notes |
|---|---|---|---|
| - | `submitted` | `task.create` passed every check | audit `task.created` only |
| `submitted` | `failed` | target has no connection | `TARGET_UNAVAILABLE` |
| `submitted` | `working` | target connected; committed before sending | sets `started_at` |
| `working` | `failed` | `task.invoke` send threw | `TARGET_UNAVAILABLE` |
| `working` | `completed`/`failed` | target's `task.result` | compare-and-set, exactly one winner |

## 3. Create: validation order

1. authenticate (agent id present) -> 2. payload shape (parser: unknown/missing/wrong-type properties, `400`) and value shape
(UUIDs, capability pattern, input is an object and within `MaxInputBytes`) -> 3. caller active -> 4. target exists (`AGENT_NOT_FOUND`,
audited) -> 5. parent rules (`TASK_NOT_FOUND`, `TASK_ACCESS_FORBIDDEN`, `TASK_STATE_CONFLICT`; not audited) -> 6. policy (domain,
capability, input schema; audited as `policy.rejected { errorCode }`) -> 7. insert + audit `task.created` in one transaction.
A rejection never creates a Task (A20).

### Ancestry (A26)

| Task | caller -> target | parent | depth | `ancestorChain` |
|---|---|---|---|---|
| T1 | A -> B | none | 0 | `[A]` |
| T2 | B -> C | T1 | 1 | `[A, B]` |
| T3 | C -> A | T2 | 2 | `[A, B, C]` |

`rootTaskId` is inherited from the parent. The chain is computed from the parent row, never from the client, and never sent on the wire.
It is stored but **not enforced** in Week 6 (T3 above is accepted).

## 4. Hub contract

- Path `/hubs/agent`, JSON protocol, camelCase, explicit nulls. Authentication reuses the `ApiKey` scheme: the key is read only from the
  `X-Api-Key` header (never the query string). Missing/wrong/revoked/registration key gives `401` and no connection.
- `Send` returns `EnvelopeAck { accepted, taskId, status, error }`, always all four members:

```json
{ "accepted": true,  "taskId": "…", "status": "working", "error": null }
{ "accepted": true,  "taskId": "…", "status": "failed",  "error": { "code": "TARGET_UNAVAILABLE", "message": "…", "details": null, "retryable": true } }
{ "accepted": false, "taskId": null, "status": null,     "error": { "code": "VALIDATION_ERROR", "message": "The request is invalid.", "details": { "errors": [ { "path": "/payload/callerAgentId", "message": "Unknown property." } ] } } }
```

- Client may send `task.create`, `task.result`, `task.status_event` (only `working`, a no-op). Anything else (unknown type, `task.invoke`,
  missing/non-object payload) is `VALIDATION_ERROR` under `/messageType` or `/payload`.
- Payload parsing is strict and hand-written (`Application/Tasks/EnvelopePayloadParser.cs`): all violations are collected with exact
  JSON Pointer paths. `callerAgentId` is never accepted from a payload.
- An unexpected exception inside `Send` gives `accepted:false` + `INTERNAL_ERROR` and keeps the connection open. Logs never contain
  input/output, credentials or raw envelopes.

## 5. Connection registry and startup purge

Redis hash `{KeyPrefix}conn:{agentId}` with `connectionId`, `instanceId`, `connectedAt`. One connection per agent, last wins.
`Unbind` is a Lua compare-and-delete on `connectionId`, so an old connection closing never removes a newer mapping.
**SignalR silently drops messages for unknown connection ids**, so a mapping left over from a previous run would never produce
`TARGET_UNAVAILABLE`. On startup (`Program.cs`, before the app accepts connections) every mapping whose `instanceId` equals this
instance's id is deleted with `SCAN` + per-key compare-and-delete. If Redis is unreachable at startup a warning is logged and startup continues.

## 6. Persistence

Tables `tasks` and `task_status_history` (status `smallint`; `ancestor_chain`, `input`, `output`, `error` are `jsonb`). Indexes:
`(status, deadline_at)`, `caller_agent_id`, `target_agent_id`, `root_task_id`, `parent_task_id`, `task_status_history(task_id, changed_at)`.
Transitions use `ITaskRepository.TryTransitionAsync`: one `UPDATE ... WHERE id = @id AND status = @expected` (`ExecuteUpdateAsync`),
chosen over an optimistic concurrency token because check and write are a single statement and no entity must be loaded.
`TaskService.TransitionAsync` wraps CAS + history row + `task.status_changed` audit in one transaction (`IUnitOfWork.StartTransaction`).

Migration: no `Migrations` folder was present in the snapshot, so the file is not hand-written. Generate it (from the repository root):

```bash
dotnet ef migrations add AddTasks --project src/AERS/Infrastructure --startup-project src/AERS/API
```

The generated `Up` must create exactly `tasks` and `task_status_history` with the indexes above and touch no Week 4-5 table. In
Development the API applies it on startup.

## 7. Configuration

```json
{ "Aers": {
    "Instance": { "Id": "" },
    "Relay": { "MaxMessageBytes": 1048576 },
    "Tasks": { "DefaultTaskTimeoutSeconds": 300, "MaxInputBytes": 262144, "MaxOutputBytes": 262144 } } }
```

Empty `Instance:Id` falls back to `Environment.MachineName`. All are validated on start (`MaxInputBytes`/`MaxOutputBytes` <= `MaxMessageBytes`).
`DefaultTaskTimeoutSeconds` is only stored as `deadline_at`; nothing enforces it in Week 6.

## 8. Policy (evaluator, normally task B-01)

`TaskPolicyEvaluator` is pure (no repository, no Redis): domain scope (`DOMAIN_FORBIDDEN`) -> capability exists, ordinal and case-sensitive
(`CAPABILITY_NOT_FOUND`) -> input valid against `inputSchema` via `JsonSchemaInputValidator` (JsonSchema.Net, Draft 2020-12; `format`
is not asserted) -> `VALIDATION_ERROR` with paths under `/payload/input`. Not included yet: rate limit, depth, cycle (Week 8).
**A minimal implementation was added by A because A-02 cannot run without it; B-01 may overwrite these files** (interfaces are frozen).

## 9. Decisions A20-A40 (summary)

A20 policy rejection never creates a Task (OD-3 closed) | A21 hub path, `X-Api-Key` (OP-1 closed) | A22 `Send`/`Receive` | A23 message types |
A24 `EnvelopeAck` | A25 taskId UUID | A26 ancestry | A27 transitions, `working` committed first | A28 `task.result` rules | A29 result delivery best effort |
A30 registry hash | A31 instance id + purge | A32 limits | A33 `GET /tasks` PostgreSQL only | A34 error codes | A35 audit rules | A36 persistence |
A37 strict parsing | A38 unexpected exception | A39 concurrent results | A40 frozen interfaces. Full text: `01-shared-contract.md` of the week-06 package.

## 10. How to run and verify

```bash
docker compose -f src/AERS/docker-compose.dev.yml up -d
dotnet ef migrations add AddTasks --project src/AERS/Infrastructure --startup-project src/AERS/API   # once
dotnet run --project src/AERS/API                                       # Development, http://localhost:5161
dotnet test src/AERS.UnitTests
```

REST checks: `docs/week-06-smoke.http`. Hub checks use the HubProbe harness (`src/AERS.HubProbe`, not the Dummy Agent):

```bash
dotnet run --project src/AERS.HubProbe -- serve --url http://localhost:5161 --key <KeyB> --behavior echo
dotnet run --project src/AERS.HubProbe -- call  --url http://localhost:5161 --key <KeyA> --target <AgentB> --capability echo --input '{"message":"hi"}'
```

The full scripted procedure with pass criteria is in `week-06-manual-testing.md`.

## 11. Known limitations (Week 6)

No timeout enforcement (a task stays `working` if the target never answers); no reconnect recovery (a result for an offline caller is read via
`GET /tasks/{id}`); one connection per agent; no depth/cycle enforcement (A -> B -> C -> A is accepted); `working` means "invoke was sent",
not "target acknowledged"; `output` is not checked against `outputSchema`.

## 12. Files

Added: `Domain/Enums/AgentTaskStatus.cs`, `Domain/Entities/{AgentTask,TaskError,TaskStatusHistory}.cs`; `Application/DTOs/Tasks/*`,
`Application/Tasks/{EnvelopeJson,EnvelopePayloadParser}.cs`, `Application/Features/{TaskService,TaskMessageService}.cs`,
`Application/Interfaces/{Features/ITaskService,Features/ITaskMessageService,Realtime/IMessageRelay,Runtime/IConnectionRegistry,Respositories/ITaskRepository,Policies/ITaskPolicyEvaluator,Validation/IInputSchemaValidator}.cs`,
`Application/Policies/TaskPolicyEvaluator.cs`, `Application/Options/{Task,Relay,Instance}Options*.cs`, `Application/Mappings/TaskMappings.cs`,
`Infrastructure/Persistences/EntityConfigurations/{AgentTask,TaskStatusHistory}Configuration.cs`, `Infrastructure/Persistences/Repositories/TaskRepository.cs`,
`Infrastructure/Runtime/RedisConnectionRegistry.cs`, `Infrastructure/Validation/JsonSchemaInputValidator.cs`, `API/Realtime/{AgentHub,SignalRMessageRelay}.cs`,
`API/Controllers/TasksController.cs`, `src/AERS.UnitTests`, `src/AERS.HubProbe`.
Changed: `ErrorCodes`, `ResultStatus` (+`ServiceUnavailable` -> 503), `ResultExtensions`, `IUnitOfWork`/`UnitOfWork`, `AersDbContext`, `RedisStoreBase` (+`Multiplexer`),
both `DependencyInjection` files in Application/Infrastructure/API, `Program.cs`, `appsettings.json`.

## 13. Deviations from the task files

- DTO namespace is `Application.DTOs.Tasks` (plural) so it can never shadow `System.Threading.Tasks.Task` inside `Application.DTOs`.
- Envelope payloads are parsed by a hand-written strict parser instead of a `JsonSerializer` round trip with `UnmappedMemberHandling.Disallow`:
  it reports every violation at once with exact paths and cannot leak .NET type names. The strictness rules of A37 are unchanged.
- `DispatchAsync` fails the task with `TARGET_UNAVAILABLE` when the connection registry (Redis) is down, instead of letting the exception escape and
  leave the task `submitted` forever.
- The startup purge is a step in `Program.cs` (allowed by A-04) rather than a hosted service, which makes its order relative to `Run()` explicit.
- The hub passes `CancellationToken.None` to `HandleAsync` so a caller disconnecting mid-`task.create` cannot leave a half-created task.
- Integration tests with Testcontainers (CAS race on real PostgreSQL, Redis registry semantics, hub authentication) are task B-04 (IT-W6-xx);
  A provides unit tests with an in-memory CAS model and the manual procedure instead.
- B-01 (`ITaskPolicyEvaluator`, `IInputSchemaValidator` and implementations) was implemented minimally by A as a prerequisite.
- The solution/`Migrations` files were not part of the snapshot; add `AERS.UnitTests` and `AERS.HubProbe` to your solution (`dotnet sln add`).
- **Nothing was compiled or run**: the authoring environment had no .NET SDK. Run `dotnet build` and `dotnet test` first and fix any compile error before review.

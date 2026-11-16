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

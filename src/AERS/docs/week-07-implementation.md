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

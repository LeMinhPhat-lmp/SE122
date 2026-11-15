# Week 7 manual testing (member A)

Prerequisites: PostgreSQL and Redis running (`docker compose -f docker-compose.dev.yml up -d`), migrations applied (see
`week-07-implementation.md`, section "Things that go beyond"), the API running on `http://localhost:5161`, and three registered agents
A, B, C in the same domain, each with the capability `echo`. Keys are called `$KEY_A`, `$KEY_B`, `$KEY_C`; ids `$A`, `$B`, `$C`.
For quick timeouts start the API with `Aers__Tasks__DefaultTaskTimeoutSeconds=15 Aers__Tasks__TimeoutScanIntervalSeconds=2
Aers__Tasks__TargetDisconnectGraceSeconds=8`. Probe: `dotnet run --project src/AERS.HubProbe -- <command> ...`.

Useful queries:

```sql
select id, status, acknowledged_at, deadline_at, error->>'code' as error_code from tasks order by created_at desc limit 5;
select event_type, details from audit_events order by id desc limit 5;
```

| # | Scenario | Steps | Proof |
|---|---|---|---|
| T1 | Timeout of a silent target | `serve` B `--behavior silent`; `call` A to B `--wait 25` | A receives `task.result` failed `TASK_TIMEOUT` about 15-17 s after the call; row `failed`, `error_code = TASK_TIMEOUT`; one `task.status_changed` audit row working to failed; Redis `HGETALL aers:task:<id>` shows `status failed`, `TTL` about 60 |
| T2 | Redelivery | `serve` B `--behavior slow --delay-ms 3000`; `call` A to B `--wait 0`, then stop A; wait for B to finish; start `watch` for A (no auto-ack) | A receives `task.result` completed on connect; `acknowledged_at` is still null; stop and restart `watch`: the result arrives again. Run `ack --task <id>` as A: `acknowledged_at` is set; restart `watch`: nothing arrives |
| T3 | Ack rules | `ack` as B for the same task; `ack` for a working task; `ack` with an unknown id | `TASK_ACCESS_FORBIDDEN`; `TASK_STATE_CONFLICT`; `TASK_NOT_FOUND`. Repeating the valid ack is accepted and does not change `acknowledged_at` |
| T4 | Target drops and returns in time | `serve` B `--behavior reconnect --reconnect-after-ms 3000`; `call` A to B | B reconnects and answers; task `completed`; no `TARGET_UNAVAILABLE` |
| T5 | Target never returns | `serve` B `--behavior silent`, then Ctrl+C B within a second | after about 8 s A receives `failed` with `TARGET_UNAVAILABLE`, retryable true (before the 15 s deadline); `ZRANGE aers:disconnected 0 -1` is empty afterwards |
| T6 | Child deadline | B `--behavior forward --forward-target $C` with C `--behavior slow --delay-ms 12000`; `call` A to B, wait | child `deadline_at` is not later than the parent `deadline_at` (query both rows) |
| T7 | Rate limit | `call` A to B `--repeat 105 --wait 0` | acks 1-100 accepted; 101+ rejected `RATE_LIMITED` with `details {limit:100, remaining:0, resetAt}` and `retryable: true`; audit `policy.rejected`; `GET aers:ratelimit:<A>:<window>` is above 100; after `resetAt` calls work again |
| T8 | Limiter outage | stop Redis, `call` A to B | ack `INTERNAL_ERROR`, retryable true, no new task row. Start Redis again: works |
| T9 | Cycle and depth | `serve` B `forward -> C`, C `forward -> A`; A `call` to B | C's child is rejected: C returns `CHILD_REJECTED`; audit `policy.rejected` with `CYCLE_DETECTED`; no task A to ... to A. For depth set `Aers__Policy__MaxRecursionDepth=1` and chain A, B, C, D | `DEPTH_EXCEEDED` with `{maxDepth:1, depth:2}` |
| T10 | Orphan hint | B `--behavior orphan-root --forward-target $C`; `call` A to B | audit `task.created` of the root task from B has `orphanSuspect: true`; ordinary tasks do not have the member |
| T11 | Revocation | `serve` B; `DELETE /agents/$B` with `$KEY_B` | B's probe disconnects within about a second; reconnecting with `$KEY_B` gets 401; with two API instances behind the same Redis the connection is aborted on whichever instance holds it |
| T12 | Auth failure log | 30 requests with a wrong `X-Api-Key` from one address | at most one warning per 10 s with `reason unknown_or_revoked`; no key value in the log; no new `audit_events` rows |
| T13 | Output warning | B answers with output that violates the capability `outputSchema` | the task is `completed`; one warning in the API log with the task id and capability, without the output |

Cleanup: `FLUSHDB` on the dev Redis removes the runtime keys; the database rows are harmless.

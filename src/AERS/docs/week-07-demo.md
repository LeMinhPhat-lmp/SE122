# Week 7 Dummy Agent demo

The scenario runner is available through `AERS.DummyAgent.Core` and keeps
network/server concerns outside the client library. Use a short server timeout
for local demos:

```bash
Aers__Tasks__DefaultTaskTimeoutSeconds=8 \
Aers__Tasks__TimeoutScanIntervalSeconds=1 \
dotnet run --project src/AERS/API
```

The deterministic behavior names are `echo`, `slow`, `silent`,
`drop-after-receive`, `always_fail`, `analyze_cv`, and `chain`.

| Scenario | Expected final line |
|---|---|
| `async-ab` | `PASS async-ab: completed` |
| `caller-offline` | `PASS caller-offline: result handled once` |
| `timeout` | `PASS timeout: TASK_TIMEOUT` |
| `target-reconnect` | `PASS target-reconnect: completed` |
| `target-vanishes` | `PASS target-vanishes: TASK_TIMEOUT` |
| `child-deadline` | `PASS child-deadline: child expired no later than parent` |
| `chain-abc` | `PASS chain-abc: completed` |
| `cycle` | `PASS cycle: CYCLE_DETECTED` |
| `depth` | `PASS depth: DEPTH_EXCEEDED` |
| `rate-limit` | `PASS rate-limit: RATE_LIMITED` |
| `cross-domain` | `PASS cross-domain: DOMAIN_FORBIDDEN` |
| `orphan-root` | `PASS orphan-root: accepted with orphanSuspect audit` |

`target-vanishes` follows **PROVISIONAL ADR-W7-03** and therefore waits for
the configured disconnect grace or task timeout, depending on the server
implementation. `rate-limit`, `orphan-root`, and credential-revocation
expectations likewise follow the provisional defaults selected for B-01/B-04.


# Week 7 demo script

This script is designed to fit in under 12 minutes. The current Dummy Agent
scenario command is a deterministic dry-run: it validates the behavior
topology, but it does not claim PostgreSQL, Redis, or SignalR persistence.

## 1. Start infrastructure and API

From the repository root:

```bash
docker compose -f docker-compose.dev.yml up -d postgres redis

Database__ConnectionString='Host=localhost;Port=5432;Database=aers;Username=aers;Password=aers' \
Aers__Redis__ConnectionString='localhost:6379' \
Aers__Auth__RegistrationKeys__0='demo-registration-key' \
Aers__Tasks__DefaultTaskTimeoutSeconds=8 \
Aers__Tasks__TimeoutScanIntervalSeconds=1 \
ASPNETCORE_URLS='http://localhost:5161' \
dotnet run --project src/AERS/API
```

In a second terminal, set:

```bash
export AERS_URL=http://localhost:5161
export REGISTRATION_KEY=demo-registration-key
```

## 2. Run the deterministic scenarios

Run each command from the repository root:

```bash
dotnet run --project src/AERS.DummyAgent -- scenario async-ab
dotnet run --project src/AERS.DummyAgent -- scenario caller-offline
dotnet run --project src/AERS.DummyAgent -- scenario timeout
dotnet run --project src/AERS.DummyAgent -- scenario target-reconnect
dotnet run --project src/AERS.DummyAgent -- scenario chain-abc
dotnet run --project src/AERS.DummyAgent -- scenario cycle
dotnet run --project src/AERS.DummyAgent -- scenario depth
dotnet run --project src/AERS.DummyAgent -- scenario rate-limit
dotnet run --project src/AERS.DummyAgent -- scenario revoked-key
```

Each command must end with the actual dry-run line:

```text
PASS <scenario>: behavior topology validated; run against AERS for persistence and timeout assertions.
```

Point at the scenario name and the `PASS` line. For a live persistence demo,
use the corresponding `HubProbe`/Dummy Agent flow and then inspect the
following evidence:

| Scenario | What to point at | Backup evidence if the live flow fails |
|---|---|---|
| `async-ab` | caller result and `task_status_history` | `SELECT id,status FROM tasks ORDER BY created_at DESC LIMIT 1;` |
| `caller-offline` | reconnect/result behavior | `SELECT id,status,caller_agent_id FROM tasks ORDER BY created_at DESC LIMIT 1;` |
| `timeout` | API log and failed task | `SELECT id,status,error,completed_at FROM tasks WHERE status=3 ORDER BY completed_at DESC LIMIT 1;` |
| `target-reconnect` | new connection and result | `GET /tasks/{id}` plus `redis-cli HGETALL aers:task:{id}` |
| `chain-abc` | parent/child IDs | `SELECT id,parent_task_id,depth,ancestor_chain FROM tasks ORDER BY created_at DESC LIMIT 3;` |
| `cycle` | rejection code | `SELECT event_type,details FROM audit_events WHERE event_type='policy.rejected' ORDER BY occurred_at DESC LIMIT 1;` |
| `depth` | rejection code | same `policy.rejected` query, checking `errorCode` |
| `rate-limit` | `RATE_LIMITED` response | `redis-cli --scan --pattern 'aers:ratelimit:*'` |
| `revoked-key` | 401 response and closed connection | `GET /agents` with the old key must return 401 |

These queries are evidence commands, not substitutes for a failed scenario:
the current server does not yet implement every Week 7/8 runtime behavior.

## 3. Register the JobHunt reference cards

With the API still running:

```bash
curl -sS -X POST "$AERS_URL/agents/register" \
  -H "X-Registration-Key: $REGISTRATION_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @agents/jobhunt/job-search.agent-card.json
curl -sS -X POST "$AERS_URL/agents/register" \
  -H "X-Registration-Key: $REGISTRATION_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @agents/jobhunt/cv-analysis.agent-card.json
curl -sS -X POST "$AERS_URL/agents/register" \
  -H "X-Registration-Key: $REGISTRATION_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @agents/jobhunt/company-research.agent-card.json
curl -sS -X POST "$AERS_URL/agents/register" \
  -H "X-Registration-Key: $REGISTRATION_KEY" \
  -H "Content-Type: application/json" \
  --data-binary @agents/jobhunt/recommendation.agent-card.json
curl -sS "$AERS_URL/agents?domain=jobhunt" \
  -H "X-Api-Key: $REGISTRATION_KEY"
```

Expected manual result: four `201` responses, followed by a discovery response
containing the four JobHunt agents and their capabilities. The same requests
are available in [register-all.http](../../../agents/jobhunt/register-all.http).

## Known limitations

The current merged implementation does not provide invoke redelivery, child
cancellation, or a server-backed execution for every scenario. Runtime state
is write-only, and depth/cycle enforcement is not wired into the task pipeline
until the relevant Week 8 work is delivered. ADR-dependent behavior remains
PROVISIONAL because the referenced ADR files are not Accepted.

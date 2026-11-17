# Week 7 regression checklist

## Smoke cases

| Area | Case | Pass criterion |
|---|---|---|
| Week 4 | Register an agent | Registration returns 201 and stores capabilities. |
| Week 4 | Heartbeat | A valid key returns 200; an invalid key returns 401. |
| Week 5 | Agent discovery | `GET /agents` returns deterministic ordering and a null cursor at the end. |
| Week 5 | Capability schemas | `GET /agents/{id}/capabilities` returns the raw input/output schemas. |
| Week 6 | Task create | A valid `task.create` returns an accepted ack with a task ID. |
| Week 6 | Target unavailable | An unavailable target returns `TARGET_UNAVAILABLE` and creates no working task. |
| Week 6 | Policy rejection | Invalid input/domain/capability returns `VALIDATION_ERROR` or `DOMAIN_FORBIDDEN`, with no task row. |
| Week 6 | Query authorization | A missing task returns 404 before authorization can return 403. |
| Week 7 | AgentClient async | `AERS.AgentClient` builds and its async tests pass. |
| Week 7 | Dummy scenarios | Each supported scenario returns the deterministic dry-run `PASS` line. |
| Week 7 | JobHunt contracts | All cards, schemas, valid examples, and invalid examples pass the contract test. |

## Commands

Run the unit and contract suites:

```bash
mise exec dotnet -- dotnet test test/AERS.UnitTests/AERS.UnitTests.csproj --no-restore
```

Run PostgreSQL/Redis integration tests when Docker is available:

```bash
AERS_RUN_INTEGRATION_TESTS=1 \
mise exec dotnet -- dotnet test test/AERS.UnitTests/AERS.UnitTests.csproj --no-restore
```

Run the manual API smoke scripts with the API at `http://localhost:5161`:

```text
src/AERS/docs/week-05-smoke.http
src/AERS/docs/week-06-smoke.http
agents/jobhunt/register-all.http
```

## Definition of Done

| Done | Evidence |
|---|---|
| [ ] Week 7 implementation notes are updated | command output / document review |
| [ ] Rate limiter behavior and options are documented | test ID / Redis output |
| [ ] Recursion policy behavior is documented | test ID / audit row |
| [ ] Async AgentClient tests pass | `AgentClientAsyncTests` output |
| [ ] Dummy Agent scenarios are listed and runnable | scenario command output |
| [ ] JobHunt cards register and schemas validate | `ReferenceAgentContractTests` |
| [ ] Demo completes in under 12 minutes | screen recording name |
| [ ] Regression suite passes | `dotnet test` output |
| [ ] Known provisional ADR behavior is called out | document review |

## Reporting

For each unchecked item, record the command, test ID, or screenshot in the
Evidence column rather than treating a skipped integration test as a pass.

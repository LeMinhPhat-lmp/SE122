# Week 5 Implementation Guide: Capability Discovery (server side)

Source of truth for behavior: `Protocol-Specification-v3.md` (R2) plus the Week 5 contract (decisions A11-A19, Spec R3 delta).
Tasks B-01 to B-05 (domain policy implementation, Agent client, Dummy Agent scenario, integration tests, spec sync) add
their own sections to this file; they are not repeated here.

## 1. Overview

Week 5 adds two read endpoints and moves the cross-domain decision into one place.

| Endpoint | Credential | Success | Errors |
|---|---|---|---|
| `GET /agents` | `X-Api-Key` | `200` + `AgentDiscoveryResponse` | 400, 401, 403, 500 |
| `GET /agents/{agentId}/capabilities` | `X-Api-Key` | `200` + `AgentCapabilitiesResponse` | 400, 401, 403, 404 |
| `GET /agents/{agentId}` (Week 4) | `X-Api-Key` | unchanged | unchanged; domain decision now uses `IDomainAccessPolicy` |

Error handling follows the Week 4 style (`Result` / `ResultExtensions`), not exceptions. See "Known deviations".

## 2. Request flow

### `GET /agents`

1. Authentication (`X-Api-Key`). A registration key, missing key, revoked key or deregistered agent gives `401 AUTHENTICATION_FAILED`.
   The service also returns 401 if the context has no agent id or domain.
2. Query validation and normalization in `AgentRegistryService.ListAsync`; every violation is collected into one
   `400 VALIDATION_ERROR` (`details.errors[].path` = `/domain`, `/capability`, `/limit`, `/cursor`).
   Type-conversion failures (`onlineOnly=abc`, `limit=abc`) are caught earlier by model binding and use the same
   error body (paths `/onlineOnly`, `/limit`).
3. `effectiveDomain = domain ?? callerDomain`; `IDomainAccessPolicy.CanAccess(callerDomain, effectiveDomain)` else `403 DOMAIN_FORBIDDEN`.
4. `IAgentRepository.ListActiveAsync(effectiveDomain, capability, after, limit)` reads `limit + 1` rows.
5. If `onlineOnly=true`, `IAgentPresenceStore.GetOnlineStatusAsync` checks the page in one Redis batch and offline agents are dropped.
   Redis is not touched when `onlineOnly=false`.
6. `nextCursor` is computed and the page is mapped to `AgentCardResponse` (capabilities ordered by name, ordinal).

The caller's own agent is included when it matches the filters. An empty result is `200` with `agents: []`.

### `GET /agents/{agentId}/capabilities`

1. Authentication (401). A malformed `{agentId}` is rejected by model binding (`400 VALIDATION_ERROR`, same as Week 4).
2. Load the active agent with its capabilities; missing or deregistered gives `404 AGENT_NOT_FOUND`.
3. Allowed when the caller is that agent or `CanAccess(callerDomain, agent.Domain)`; otherwise `403 DOMAIN_FORBIDDEN`. 404 is evaluated before 403.
4. `200 { agentId, capabilities[] }`, ordered by `name` (ordinal).

## 3. Pagination design

- **Why keyset:** offset pagination skips or repeats rows when agents register or deregister between pages and gets slower with depth.
  Keyset on `(registered_at, id)` is stable and index-friendly.
- **Cursor:** base64url without padding of `{"v":1,"t":"<UTC, 6 fractional digits>","id":"<uuid>"}`, built and parsed only by `Application/Discovery/DiscoveryCursor.cs`.
  Clients treat it as opaque. It is not signed: scope and access are re-evaluated on each request, so a forged cursor cannot widen access.
- **`limit + 1`:** the repository fetches one extra row. `HasMore = rows > limit`; `Items` never contains the extra row.
- **`nextCursor`:** built from the last row scanned in the page (the `limit`-th row), before the `onlineOnly` filter, so it always advances.
  It is `null` when no further rows exist and is always serialized (explicit `null`).
- **`onlineOnly` short pages:** a page can contain fewer than `limit` agents, even zero, while `nextCursor` is non-null.
  This is accepted because filtering after the page was read keeps the query simple and the cursor monotonic; clients follow `nextCursor` until it is `null`.
- **Precision:** PostgreSQL `timestamptz` has microsecond precision. The cursor timestamp is taken from entities read from the
  database, never from an in-memory `UtcNow`, so the comparison `(registered_at, id) > (t, id)` neither repeats nor skips a row.
- **Guarantees:** no duplicates and no skipped rows for agents that exist for the whole traversal; agents registered during
  traversal may or may not appear; agents deregistered during traversal disappear.

## 4. Data access

`AgentRepository.ListActiveAsync` (Infrastructure):

- Base filter: `deregistered_at IS NULL AND domain = @domain`.
- Capability filter (optional): `Any(capability => capability.Name == name && capability.DeletedAt == null)`, translated to `EXISTS` in SQL, case-sensitive.
- Keyset predicate for pages after the first: the query starts from `FromSql` with the row-value comparison
  `(registered_at, id) > (@t, @id)`. Raw SQL is used on purpose: the `id` comparison and the ordering must both run in
  PostgreSQL, because .NET `Guid` ordering differs from PostgreSQL `uuid` ordering, and `Guid.CompareTo` is not guaranteed to translate.
  The first page uses plain LINQ. Both branches share the same filters, order and `Take`.
- Order: `ORDER BY registered_at, id`; `Take(limit + 1)`.
- Capabilities are loaded with a filtered `Include` and `AsSplitQuery()` (avoids cartesian explosion with `Take`): two SQL statements per call, no N+1.
  The query uses `AsNoTracking()`.

Index (`AgentConfiguration`): partial composite index `ix_agents_domain_registered_at_id` on `(domain, registered_at, id)`
with filter `deregistered_at IS NULL`. Column order: equality column first (`domain`), then the sort columns in the same
order as `ORDER BY`, so PostgreSQL can walk the index in order and stop after `limit + 1` rows. The filter matches the
query's soft-delete filter, so deregistered rows stay out of the index.

`agent_capabilities` already has the unique index `(agent_id, name)` from Week 4 (used by the capability filter).

## 5. Redis usage

`RedisAgentPresenceStore.GetOnlineStatusAsync(ids)`:

- Key per agent: `{KeyPrefix}agent:{id}:alive`, built by the same `AliveKey` method used for write, read and delete.
- One `IBatch`: `KeyExistsAsync` is queued per distinct id, `Execute()` sends them in one round trip, then all tasks are awaited.
- Empty input returns an empty dictionary without calling Redis. Every requested id is in the result (`false` = offline).
- The `lastbeat` key is not read; the list never returns `lastHeartbeatAt`.
- Redis is only touched when `onlineOnly=true`. A Redis failure is translated by `RedisStoreBase` into `StateStoreUnavailableException`, is not caught in
  the service, and becomes `500 INTERNAL_ERROR` through the global handler (decision A5).

## 6. Domain access

`IDomainAccessPolicy.CanAccess(callerDomain, targetDomain)` (Application) is the only place that decides domain access.
`GetAsync`, `ListAsync` and `GetCapabilitiesAsync` call it. An agent reading itself is checked in the service (`caller.AgentId == agent.Id`) before the policy.

Rules: same domain, or the directed pair `(callerDomain, targetDomain)` listed in `Aers:Policy:DomainPolicy:CrossDomainRules`.
`DenyByDefault` is the shipped mode and `appsettings.json` has an empty rule list. The sample rule `hr -> jobhunt`
exists only in `appsettings.Development.json` (and in the test factory). Rules are directed: `hr` agents can read `jobhunt`, not the reverse.

**Status:** until task B-01 is merged, `Application/Policies/TemporaryDomainAccessPolicy.cs` implements the interface (the Week 4 inline rule, unchanged).
Task A-05 deletes it and registers the B-01 implementation (one line in `Application/Extensions/DependencyInjection.cs`).

## 7. Configuration reference

```json
{
  "Aers": {
    "Policy": { "DomainPolicy": { "Mode": "DenyByDefault", "CrossDomainRules": [] } },
    "Discovery": { "DefaultPageSize": 50, "MaxPageSize": 100 }
  }
}
```

| Key | Default | Notes |
|---|---|---|
| `Aers:Discovery:DefaultPageSize` | 50 | must be in `1..MaxPageSize` |
| `Aers:Discovery:MaxPageSize` | 100 | must be `>= 1` |
| `Aers:Policy:DomainPolicy:Mode` | `DenyByDefault` | validation of mode/domain values is part of B-01 |
| `Aers:Policy:DomainPolicy:CrossDomainRules` | empty | directed pairs |

`DiscoveryOptions` is validated on start (`ValidateOnStart`); the app refuses to start with an invalid page-size configuration.

## 8. How to run and verify

```bash
docker compose -f src/AERS/docker-compose.dev.yml up -d      # PostgreSQL + Redis
dotnet build SE122.slnx
dotnet test SE122.slnx                                       # unit tests (test/AERS.UnitTests)
dotnet run --project src/AERS/API                            # Development: applies migrations automatically
```

Migration for the new index (the repository snapshot had no `Migrations` folder, so generate it where your Week 4 migration lives):

```bash
dotnet ef migrations add AddAgentDiscoveryIndex --project src/AERS/Infrastructure --startup-project src/AERS/API
```

Check that the generated `Up` creates `ix_agents_domain_registered_at_id` on `(domain, registered_at, id)` with filter `deregistered_at IS NULL`.

Then run `docs/week-05-smoke.http` top to bottom. Expected: requests 3, 4, 7, 8 return `200`; 5 returns an empty list; 6 and 9 return
`403 DOMAIN_FORBIDDEN`; 10 returns `404`; 11, 15, 16 return `400`; 17 and 18 return `401`. Request 12 with `limit=1` returns a non-null `nextCursor` when the domain has at least two agents.

Manual repository checks (B-04 automates them):

1. Seed at least 5 agents in one domain and page with `limit=2`: three pages, no duplicates, stable order.
2. Set the same `registered_at` on two agents directly in the database and confirm the tie-break by `id` across a page boundary.
3. `EXPLAIN` the query for a domain with many rows and confirm the new index is used. Log the SQL once and confirm two statements per call.

## 9. Decisions A11-A19

| ID | Decision |
|---|---|
| A11 | `GET /agents` without `domain` lists only the caller's own domain (from the credential). |
| A12 | Explicit `domain=X` only if `X` is the caller's domain or the directed pair is allowlisted; else `403 DOMAIN_FORBIDDEN`. |
| A13 | `onlineOnly` defaults to `false`; the list never shows online/offline status. |
| A14 | The `capability` filter is an exact, case-sensitive match on the capability name. |
| A15 | `GET /agents/{id}/capabilities` returns `AgentCapabilitiesResponse { agentId, capabilities[] }`. |
| A16 | Cursor (keyset) pagination: `limit` default 50 / max 100, opaque `cursor`, `nextCursor`; short pages possible with `onlineOnly`. |
| A17 | Deterministic order: agents by `registered_at, id`; capabilities by name (ordinal); 404 before 403. |
| A18 | Invalid query values give `400 VALIDATION_ERROR`; order is 401, 400, 403, data access. |
| A19 | `IAgentRegistry` gains list and capabilities operations; domain access is decided only in `IDomainAccessPolicy`. |

## 10. Known limitations and deviations

Limitations:

- Each `AgentCard` carries all capability schemas, so a page can be large. A summary mode is a possible later change.
- A forged cursor is harmless (access is re-evaluated) but can make the caller skip or repeat rows in its own traversal.
- With `onlineOnly=true` a page can be short or empty while `nextCursor` is set.

Deviations from the task files (the code base was the source of truth):

- **Result instead of `AersException`:** the repository uses `Result` / `Result<T>` and `ResultExtensions`; the new methods follow it (decision taken with the owner).
- **Names:** the interface is `IAgentRegistryService` (contract name `IAgentRegistry`) and the service lives in `Application.Services`. Both new methods are on `IAgentRegistryService`.
- **DTOs:** Week 4 returns Application response DTOs directly from controllers, so no separate API wire DTOs were added. The contract's `AgentPageDto` and
  `AgentCapabilitiesDto` are `AgentDiscoveryResponse` and `AgentCapabilitiesResponse`; the existing `AgentCardResponse` / `CapabilityResponse` are reused. The only API-layer
  type added is the query binder `DiscoveryQueryRequest`.
- **Single-agent lookup:** `GetWithCapabilitiesAsync` from Week 4 is reused instead of adding `FindActiveWithCapabilitiesAsync`.
- **Migration:** not generated in the repository (no migrations folder in the snapshot); the index is configured in `AgentConfiguration` and the command is above.
- **Swagger:** the project uses the built-in OpenAPI (`/openapi/v1.json` in Development), documented with `ProducesResponseType`, `EndpointSummary` and `EndpointDescription`.
- **Temporary policy:** `TemporaryDomainAccessPolicy` stands in for the B-01 implementation until A-05 completes.
- **Tests:** the snapshot had no test project, so `test/AERS.UnitTests` was created (xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1; hand-written fakes, no mocking library).
  Redis and PostgreSQL behavior is left to the B-04 integration tests.
- **Not executed:** the code was written without a .NET SDK available; build and run the tests before merging.

## 11. Files added/changed

| Path | Change | Purpose |
|---|---|---|
| `Application/DTOs/Agent/Requests/AgentQuery.cs` | new | Raw query of `GET /agents` |
| `Application/DTOs/Agent/Responses/AgentDiscoveryResponse.cs` | new | List response with `nextCursor` |
| `Application/DTOs/Agent/Responses/AgentCapabilitiesResponse.cs` | new | Capabilities response |
| `Application/Interfaces/Policies/IDomainAccessPolicy.cs` | new | Single domain access decision |
| `Application/Policies/TemporaryDomainAccessPolicy.cs` | new | Temporary implementation until B-01 |
| `Application/Interfaces/Respositories/AgentPage.cs` | new | `AgentPage`, `DiscoveryCursorPosition` |
| `Application/Interfaces/Respositories/IAgentRepository.cs` | changed | `ListActiveAsync` |
| `Application/Interfaces/Runtime/IAgentPresenceStore.cs` | changed | `GetOnlineStatusAsync` |
| `Application/Interfaces/Features/IAgentRegistryService.cs` | changed | `ListAsync`, `GetCapabilitiesAsync` |
| `Application/Features/AgentRegistryService.cs` | changed | New use cases; `GetAsync` uses the policy |
| `Application/Discovery/DiscoveryCursor.cs` | new | Cursor codec |
| `Application/Options/DiscoveryOptions.cs` | new | `Aers:Discovery` |
| `Application/Mappings/AgentMappings.cs` | changed | `ToCapabilityResponses` |
| `Application/Extensions/DependencyInjection.cs` | changed | Options validation, policy registration |
| `Infrastructure/Persistences/Repositories/AgentRepository.cs` | changed | Keyset query |
| `Infrastructure/Persistences/EntityConfigurations/AgentConfiguration.cs` | changed | Partial composite index |
| `Infrastructure/Runtime/RedisAgentPresenceStore.cs` | changed | Batched presence lookup |
| `API/Controllers/AgentsController.cs` | changed | Two new endpoints |
| `API/DTOs/ApiContract/Requests/DiscoveryQueryRequest.cs` | new | Query binder |
| `API/appsettings.json`, `API/appsettings.Development.json` | changed | Discovery options; dev sample rule `hr -> jobhunt` |
| `docs/week-05-smoke.http` | new | Smoke script |
| `docs/week-05-implementation.md` | new | This document |
| `test/AERS.UnitTests/*` | new | Unit tests |
| `SE122.slnx` | changed | Adds the test project |

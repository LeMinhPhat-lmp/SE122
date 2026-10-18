# Week 4 Implementation Guide: Agent Registry and Registration

This document explains what the Week 4 code does, where each piece lives, and why it is built this way.
Source of truth for behavior: `Protocol-Specification-v3.md` (R2) and `05-decision-log-v3.md`.

## 1. Scope

| Endpoint | Credential | Success | Errors |
|---|---|---|---|
| `POST /agents/register` | `X-Registration-Key` | `201` + `{ agentId, apiKey }` | 400, 401 |
| `POST /agents/{agentId}/heartbeat` | `X-Api-Key` | `204` | 400, 401, 403, 404, 500 |
| `GET /agents/{agentId}` | `X-Api-Key` | `200` + `AgentDetailResponse` | 400, 401, 403, 404, 500 |
| `DELETE /agents/{agentId}` | `X-Api-Key` | `204` | 400, 401, 403, 404 |
| `GET /health` | none | `200` | 503 |

Not in Week 4: discovery lists (`GET /agents`, `GET /agents/{id}/capabilities`), tasks, SignalR.

## 2. Layers and dependency direction

```text
API  ->  Application  ->  Domain
 |            ^
 +-> Infrastructure (implements Application interfaces, references Application and Domain)
```

DTO placement (temporary decision, CQRS split skipped to save effort):

- `Application/DTOs`: every request and response that is exposed to clients.
- `API/DTOs/ApiContract`: contracts used only inside the API layer (`ErrorResponse`, `ValidationErrorItem`, `ValidationErrorDetails`).

## 3. Request flows

### Register

```text
Client                API                                Application                    Infrastructure
  | POST /agents/register
  |--------------------> RegistrationKey auth handler
  |                      model binding (strict JSON)
  |                      AgentsController.RegisterAgentAsync
  |                                                       AgentRegistryService.RegisterAsync
  |                                                         trim name, lowercase domain
  |                                                         FluentValidation (throws on failure)
  |                                                         new Guid (UUID v4) + new API key
  |                                                         agent + capabilities + credential(hash)
  |                                                         + audit event -> one SaveChanges --> PostgreSQL
  |                                                         mark online (best effort) ----------> Redis
  |<-- 201 { agentId, apiKey } + Location + Cache-Control: no-store
```

### Heartbeat, Get, Deregister

- Authentication resolves `X-Api-Key` to an agent: the key is hashed (SHA-256) and looked up among credentials that are
  not revoked and belong to a non-deregistered agent. The agent id and domain become claims.
- Heartbeat and Deregister require that the route id equals the authenticated agent id, otherwise `403 AGENT_ACCESS_FORBIDDEN`.
- Heartbeat refreshes two Redis keys (see section 6). Deregister sets `deregistered_at`, revokes all credentials of the
  agent, writes an audit event, then removes the Redis keys.
- Get checks existence first (404), then access (403 `DOMAIN_FORBIDDEN`): allowed for the agent itself, the same domain,
  or a domain pair listed in `Aers:Policy:DomainPolicy:CrossDomainRules`.

## 4. Error handling model

Three mechanisms, each with a clear owner:

1. **Expected outcomes are `Result`.** `Result` and `Result<T>` (with `ResultStatus`, `ErrorCode` and `ErrorDetails`) are the
   contract between the Application and API layers. Services return them for outcomes such as not found, forbidden and
   validation failure. `ResultExtensions` in the API layer is the only place that turns a failed `Result` into HTTP.
2. **Technical failures with business meaning are wrapped by the layer that understands them.** Infrastructure catches
   `RedisException` once, in `RedisStoreBase`, and throws `StateStoreUnavailableException` (an `ApplicationException`
   with the Redis exception as inner exception). The Application layer then decides what it means: registration and
   deregistration catch it (the database has already committed), heartbeat and get let it through.
3. **Everything else goes to `GlobalExceptionHandler`.** It asks `IErrorClassifier` what the exception means, logs
   internal errors, and writes the response with `WriteErrorAsync`.

`ErrorClassifier` is a chain of responsibility over `IExceptionClassifier` implementations. Each layer registers the
classifier for the exceptions only it knows about:

| Classifier | Layer | Recognizes | Result |
|---|---|---|---|
| `ApplicationExceptionClassifier` | Application | `ApplicationException` and subclasses | status and code carried by the exception (`StateStoreUnavailableException` gives 500 `INTERNAL_ERROR`) |
| `BadHttpRequestExceptionClassifier` | API | `BadHttpRequestException` (body too large or unreadable) | 400 `VALIDATION_ERROR` |
| fallback in `ErrorClassifier` | Application | anything else | 500 `INTERNAL_ERROR` with a generic message |

The classifier also inspects inner exceptions, so a wrapped exception is recognized by the layer that understands it.

Failures that never reach the exception handler but use the same `ResultExtensions` path:

- Model binding failures (bad JSON, unknown property, null for a required member, route id that is not a Guid) are
  handled by `InvalidModelStateResponseFactory` in `API/Extensions/DependencyInjection.cs`.
- Authentication failures are written by `AgentAuthenticationHandlerBase.HandleChallengeAsync` (401).
- FluentValidation failures are converted by `ValidationResultExtensions.ToFailure<T>()` inside the service.

Result to HTTP mapping (`ResultStatus.ToStatusCode`): `ValidationError`/`BadRequest` 400, `Unauthorized` 401,
`Forbidden` 403, `NotFound` 404, `Conflict` 409, `InternalError` 500.

Local `try/catch` blocks that remain, and why:

- `AgentRegistryService.RegisterAsync` and `DeregisterAsync` catch `StateStoreUnavailableException` only: after the
  database commit a Redis outage must not fail the request (the one-time `apiKey` would be lost, decision A5).
- `RedisStoreBase` is the single place that catches `RedisException`.
- `DependencyHealthChecker` converts connectivity failures into "unhealthy" (503).

## 5. Validation pipeline

1. Strict JSON binding (`AddJsonOptions`): unknown members are rejected (`UnmappedMemberHandling = Disallow`), missing
   required constructor parameters and nulls for non-nullable members are rejected. This is how `agentId` in a
   registration payload is refused.
2. FluentValidation inside `RegisterAsync` (rules from spec section 1.2.1): name length, domain pattern, capability count,
   capability name pattern and uniqueness, schema size, and schema validity against the Draft 2020-12 metaschema
   (`ISchemaMetaValidator`, implemented with JsonSchema.Net).
3. Limits come from `Aers:RegistrationLimits`; the body size limit is applied to Kestrel in `Program.cs`.

Known trade-off: the serializer reports the first structural error only, while FluentValidation reports all rule errors.

## 6. Redis keys (presence)

```text
{prefix}agent:{agentId}:alive      "1"       expires after OfflineThresholdSeconds (45)
{prefix}agent:{agentId}:lastbeat   ISO-8601  expires after 24 hours
```

Online means the `alive` key exists. There is no background worker; expiry does the work.

## 7. File map

### Domain
| File | Responsibility |
|---|---|
| `Entities/Agent.cs` | Aggregate root; soft delete via `DeletedAt` (column `deregistered_at`); one-to-many capabilities and credentials |
| `Entities/AgentCredential.cs` | Stores only the API key hash; `Revoke` is idempotent |
| `Entities/AuditEvent.cs` | Audit trail row without foreign keys |
| `Constants/AuditEventTypes.cs` | Event type names from spec section 6.5 |

### Application
| File | Responsibility |
|---|---|
| `Features/AgentRegistryService.cs` | All registry use cases |
| `Interfaces/Features/IAgentRegistryService.cs` | Service contract |
| `Interfaces/Security/*` | API key generation/hash, registration key check |
| `Interfaces/Runtime/*` | Presence store, dependency health check |
| `Interfaces/Validation/ISchemaMetaValidator.cs` | Draft 2020-12 metaschema check |
| `Validators/*` | FluentValidation rules |
| `DTOs/Result/*` | `Result`, `Result<T>`, `ResultStatus`, `ErrorDetail` |
| `Exceptions/*` | `ApplicationException`, `StateStoreUnavailableException`, `ErrorClassifier`, `ApplicationExceptionClassifier` |
| `Interfaces/Errors/*` | `IErrorClassifier`, `IExceptionClassifier` |
| `Extensions/ValidationResultExtensions.cs` | FluentValidation failures to a failed `Result` (reused by future services) |
| `DTOs/Agent/*` | Request and response DTOs |
| `Options/*` | `Aers:Auth`, `Aers:RegistrationLimits`, `Aers:Policy` |
| `Mappings/AgentMappings.cs` | Entity to response mapping (reused by discovery in Week 5) |

### Infrastructure
| File | Responsibility |
|---|---|
| `Persistences/AersDbContext.cs` | Snake_case columns, automatic `CreatedAt`/`UpdatedAt` |
| `Persistences/EntityConfigurations/*` | Tables, indexes, jsonb columns |
| `Persistences/Repositories/*` | Repositories and unit of work (soft-delete filters use `DeletedAt == null`) |
| `Runtime/RedisStoreBase.cs` | Template for Redis stores; translates `RedisException` into `StateStoreUnavailableException` |
| `Runtime/RedisAgentPresenceStore.cs` | Presence keys |
| `Runtime/DependencyHealthChecker.cs` | PostgreSQL and Redis probe |
| `Security/ApiKeyService.cs` | `aers_` + base64url(32 random bytes), SHA-256 hex hash |
| `Security/RegistrationKeyValidator.cs` | Constant-time comparison |
| `Validation/JsonSchemaMetaValidator.cs` | JsonSchema.Net wrapper |
| `Extensions/DependencyInjection.cs` | Service registration |

### API
| File | Responsibility |
|---|---|
| `Controllers/AgentsController.cs` | Four registry endpoints |
| `Controllers/HealthController.cs` | `GET /health` |
| `Authentication/*` | Two authentication schemes, claims to `AuthenticatedAgentContext` |
| `Exceptions/GlobalExceptionHandler.cs` | Logs and writes the response for classified exceptions |
| `Exceptions/BadHttpRequestExceptionClassifier.cs` | API-owned classifier |
| `Extensions/ResultExtensions.cs` | Failed `Result` to HTTP status, `ErrorResponse`, `ObjectResult` or direct response |
| `Extensions/DependencyInjection.cs` | JSON options, model-binding error factory, authentication setup |
| `DTOs/ApiContract/Responses/*` | `ErrorResponse` and its `details` object |
| `Program.cs` | Composition root, startup checks, Development migration |

## 8. Configuration

| Key | Meaning |
|---|---|
| `Database:*` | PostgreSQL connection settings |
| `Aers:Auth:RegistrationKeys` | Allowed registration keys; must be non-empty outside Development |
| `Aers:Redis:ConnectionString`, `KeyPrefix` | Redis connection and key prefix |
| `Aers:RegistrationLimits:*` | Name, capability, schema and body limits |
| `Aers:Policy:Heartbeat:*` | Heartbeat interval and offline threshold |
| `Aers:Policy:DomainPolicy:CrossDomainRules` | Allowed `(callerDomain, targetDomain)` pairs |

## 9. Run locally

```bash
docker compose -f docker-compose.dev.yml up -d
dotnet ef migrations add InitialCreate -p Infrastructure -s API
dotnet run --project API
```

Then use `API/API.http`. In Development the app applies migrations at startup.

## 10. Things to verify after the first build

- `JsonSchema.Net` and `StackExchange.Redis` package versions restore; adjust the versions in `Infrastructure.csproj` if not.
- `DELETE /agents/{id}` with no body and no `Content-Type` returns 204 (not 415).
- Strict JSON options do not break any response serialization (all response DTOs are annotated for nullability).
- Registering the same payload twice creates two different agents (no uniqueness by name).

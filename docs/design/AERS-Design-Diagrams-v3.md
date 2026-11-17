# AERS — Tài liệu Diagram thiết kế (Design Diagrams)

**Revision:** R3 — đồng bộ Tuần 7 (R2: đồng bộ trước Tuần 4). Tên tệp mang chỉ số bộ tài liệu `-v3`.
**Căn cứ:** `Protocol-Specification-v3.md` (nguồn ưu tiên cao nhất), `Protocol-Sequence-Diagrams-v3.md`, `02-tech-stack-v3.md`, `03-mo-ta-de-tai-v3.md`, `04-tong-quan-component-va-tham-khao-v3.md`, `05-decision-log-v3.md`, Project Charter v3, `01-ke-hoach-15-tuan-v3.md`.
**Phạm vi:** tập hợp diagram phục vụ thiết kế và triển khai; trọng tâm là Registry (Tuần 4). Khi diagram mâu thuẫn với Protocol Specification, Protocol Specification có hiệu lực.

Quy ước: ⭐ = trọng tâm Tuần 4 (Registry, Registration, Heartbeat, Deregistration). Các diagram còn lại phục vụ Tuần 5–8 và được chốt sớm nhằm tránh thay đổi cấu trúc mã nguồn/cơ sở dữ liệu về sau. Các điểm chưa chốt được đánh dấu bằng mã OD-n và theo dõi tại `05-decision-log-v3.md`.

Mục lục:

| # | Diagram | Dùng cho |
|---|---|---|
| D1 | Use Case | Phạm vi chức năng, actor |
| D2 | Component | Toàn cảnh runtime |
| D3 | Layer / Package (Clean Architecture) | Cấu trúc solution, quy tắc dependency |
| D4 | Domain Class Diagram | Entity/Value Object trong Core |
| D5 | Application Class Diagram ⭐ | Interface, handler, DTO |
| D6 | ERD PostgreSQL ⭐ | Migration EF Core |
| D7 | Redis Data Model ⭐ | Key, TTL |
| D8 | Sequence: Register ⭐ | Tuần 4 |
| D9 | Sequence: Heartbeat, Status, Deregister ⭐ | Tuần 4 |
| D10 | Sequence: Discovery | Tuần 5 |
| D11 | Sequence: Sync Task A → B (đủ pipeline) | Tuần 6–8 |
| D12 | Sequence: Async + đệ quy + cycle (đã sửa) | Tuần 7–8 |
| D13 | Sequence: Timeout worker + reconnect | Tuần 7 |
| D14 | Activity: Policy pipeline | Tuần 8 |
| D15 | State: Task + Agent | Tuần 4–7 |
| D16 | Deployment | Tuần 12 |

---

## D1. Use Case Diagram

```plantuml
@startuml AERS_UseCase
title AERS - Use Case Diagram
left to right direction

actor "Agent Developer /\nAgent (client)" as Agent
actor "Caller Agent" as Caller
actor "Target Agent" as Target
actor "AERS Operator" as Op
actor "Scheduler\n(background worker)" as Sched <<system>>

Caller -|> Agent
Target -|> Agent

rectangle "AERS" {
  package "Registry" {
    usecase "UC01 Register Agent" as UC01
    usecase "UC02 Send Heartbeat" as UC02
    usecase "UC03 Deregister Agent" as UC03
  }
  package "Discovery" {
    usecase "UC04 List / Filter Agents" as UC04
    usecase "UC05 Get Agent Card" as UC05
    usecase "UC06 Get Capabilities + Schema" as UC06
  }
  package "Task" {
    usecase "UC07 Create Task" as UC07
    usecase "UC08 Receive & Execute Task\n(task.invoke)" as UC08
    usecase "UC09 Submit Task Result / Status" as UC09
    usecase "UC10 Receive Push Notification" as UC10
    usecase "UC11 Query Task (GET /tasks/{id})" as UC11
    usecase "UC12 Create Child Task (recursive)" as UC12
  }
  package "Protection" {
    usecase "Authenticate" as UCAuth
    usecase "Check Access Policy" as UCPol
    usecase "Check Rate Limit" as UCRate
    usecase "Check Depth / Cycle" as UCCyc
  }
  package "Administration" {
    usecase "UC13 Provision credentials\n(registration key / API key)" as UC13
    usecase "UC14 Configure policy\n(appsettings, allowlist)" as UC14
    usecase "UC15 View audit events" as UC15
  }
  usecase "UC16 Detect Task Timeout" as UC16
}

Agent --> UC01
Agent --> UC02
Agent --> UC03
Agent --> UC04
Agent --> UC05
Agent --> UC06
Caller --> UC07
Caller --> UC10
Caller --> UC11
Target --> UC08
Target --> UC09
Target --> UC12
Op --> UC13
Op --> UC14
Op --> UC15
Sched --> UC16

UC01 .> UCAuth : <<include>>
UC02 .> UCAuth : <<include>>
UC03 .> UCAuth : <<include>>
UC07 .> UCAuth : <<include>>
UC07 .> UCPol : <<include>>
UC07 .> UCRate : <<include>>
UC07 .> UCCyc : <<include>>
UC12 .> UC07 : <<extend>>
UC16 .> UC09 : <<include>>

note bottom of UCPol
  AERS KHÔNG có use case "chọn Agent phù hợp".
  Đó là trách nhiệm của Agent (Dumb Relay).
end note
@enduml
```

---

## D2. Component Diagram

```plantuml
@startuml AERS_Component
title AERS - Component Diagram (runtime)

actor "Agent (client)" as Agent

node "AERS Instance" {
  package "AERS.Api" {
    [REST Controllers\n(Registration, Discovery, Tasks)] as REST
    [AgentHub (SignalR)] as Hub
    [Auth Middleware\n(JWT / API Key)] as AuthMW
  }
  package "AERS.Application" {
    [Registration Service] as RegSvc
    [Discovery Service] as DiscSvc
    [Task Manager] as TaskMgr
    [Policy Engine] as Policy
    [Message Relay] as Relay
    [Schema Validator\n(JsonSchema.Net)] as Validator
    [Timeout Scheduler\n(BackgroundService)] as Sched
  }
  package "AERS.Infrastructure" {
    [Agent Repository (EF Core)] as AgentRepo
    [Task Repository (EF Core)] as TaskRepo
    [Audit Writer] as Audit
    [Runtime State Store (Redis)] as State
    [Connection Registry (Redis)] as ConnReg
    [Rate Limiter (Redis)] as Rate
  }
}

database "PostgreSQL" as PG
database "Redis\n(+ SignalR backplane)" as Redis

Agent --> REST : HTTPS
Agent <--> Hub : SignalR (JSON envelope)
REST --> AuthMW
Hub --> AuthMW
REST --> RegSvc
REST --> DiscSvc
REST --> TaskMgr : GET /tasks/{id}
Hub --> TaskMgr
Hub --> Relay

RegSvc --> Validator
RegSvc --> AgentRepo
RegSvc --> State : set alive
RegSvc --> Audit
DiscSvc --> AgentRepo
DiscSvc --> State : online?
TaskMgr --> Policy
TaskMgr --> TaskRepo
TaskMgr --> State
TaskMgr --> Audit
Policy --> AgentRepo
Policy --> Rate
Relay --> ConnReg
Relay --> Hub : push to connection
Sched --> TaskMgr

AgentRepo --> PG
TaskRepo --> PG
Audit --> PG
State --> Redis
ConnReg --> Redis
Rate --> Redis
Hub ..> Redis : backplane
@enduml
```

---

## D3. Layer / Package Diagram (Clean Architecture)

Quy tắc: mũi tên = "phụ thuộc vào". `Core` không phụ thuộc bất kỳ layer nào.

```plantuml
@startuml AERS_Layers
title AERS - Solution Structure & Dependency Rule
skinparam packageStyle rectangle

package "src/AERS" {
  package "AERS.Core (Domain)" as Core {
    [Entities: Agent, AgentCapability, AgentTask,\nTaskStatusHistory, AuditEvent]
    [Enums & Value Objects: TaskStatus, AgentStatus,\nAncestorChain, AersError]
    [Domain rules: AgentId (UUID v4), Task transitions]
  }
  package "AERS.Application" as App {
    [Interfaces: IAgentRegistry, ITaskManager,\nIPolicyEngine, IMessageRelay, IRuntimeStateStore ...]
    [Services / Handlers]
    [DTOs (Application layer)]
    [AersPolicyOptions]
  }
  package "AERS.Infrastructure" as Infra {
    [EF Core DbContext + Migrations]
    [Redis implementations]
    [SignalR connection registry]
    [JsonSchema.Net validator]
  }
  package "AERS.Api (Host)" as Api {
    [Controllers + Hub]
    [Wire DTOs / Envelope contracts]
    [Program.cs - DI composition root]
  }
}

package "test" {
  [AERS.UnitTests (xUnit)] as UT
  [AERS.IntegrationTests\n(WebApplicationFactory + Testcontainers)] as IT
  [AERS.DummyAgent (console)] as Dummy
}

App --> Core
Infra --> App
Infra --> Core
Api --> App
Api --> Infra : chỉ để đăng ký DI
UT --> Core
UT --> App
IT --> Api
Dummy ..> Api : giao tiếp qua mạng\n(REST + SignalR), KHÔNG reference code

note bottom of Core
  Ba lớp model KHÔNG đồng nhất:
  Wire DTO (Api) -> Application DTO -> Persistence Entity (Core/Infra)
  Mapping tường minh giữa các lớp.
end note
@enduml
```

> Gợi ý đặt tên project: `AERS.Core`, `AERS.Application`, `AERS.Infrastructure`, `AERS.Api`,
> `AERS.DummyAgent`. Nếu skeleton hiện tại đặt tên khác, giữ nguyên tên trong repo và chỉ map lại.

---

## D4. Domain Class Diagram (AERS.Core)

```plantuml
@startuml AERS_Domain
title AERS.Core - Domain Model
hide empty members

enum AgentStatus {
  Online
  Offline
}
note right of AgentStatus
  Derived runtime (Redis TTL), KHÔNG lưu cột trong PostgreSQL.
end note

enum TaskStatus {
  Submitted
  Working
  Completed
  Failed
}

class Agent {
  + Id : Guid  <<UUID v4, AERS sinh>>
  + Name : string
  + Domain : string
  + Description : string
  + OwnerRef : string?
  + RegisteredAt : DateTimeOffset
  + DeregisteredAt : DateTimeOffset?
  + IsActive : bool
  --
  + AddCapability(cap)
  + Deregister(now)
}

class AgentCapability {
  + Id : Guid
  + AgentId : Guid
  + Name : string
  + Description : string
  + InputSchema : JsonDocument
  + OutputSchema : JsonDocument
}

class AgentCredential {
  + Id : Guid
  + AgentId : Guid
  + ApiKeyHash : string
  + CreatedAt : DateTimeOffset
  + RevokedAt : DateTimeOffset?
}
note right of AgentCredential
  Quyết định D-04: API key per-agent, chỉ lưu HASH (SHA-256).
  Key thô chỉ trả 1 lần trong Registration Response.
  Chi tiết: Protocol-Specification-v3.md mục 8.
end note

class AgentTask {
  + Id : Guid
  + CallerAgentId : Guid
  + TargetAgentId : Guid
  + CapabilityName : string
  + Status : TaskStatus
  + Input : JsonDocument
  + Output : JsonDocument?
  + ParentTaskId : Guid?
  + RootTaskId : Guid
  + Depth : int
  + AncestorChain : AncestorChain
  + DeadlineAt : DateTimeOffset
  + CreatedAt : DateTimeOffset
  + StartedAt : DateTimeOffset?
  + CompletedAt : DateTimeOffset?
  + AcknowledgedAt : DateTimeOffset?  <<chỉ persistence, ADR-W7-02>>
  + Error : AersError?
  --
  + MarkWorking(now)
  + MarkCompleted(output, now)
  + MarkFailed(error, now)
  + CreateChild(target, capability, input, now) : AgentTask
}

class TaskStatusHistory {
  + Id : long
  + TaskId : Guid
  + FromStatus : TaskStatus?
  + ToStatus : TaskStatus
  + ChangedAt : DateTimeOffset
}

class AuditEvent {
  + Id : long
  + EventType : string
  + OccurredAt : DateTimeOffset
  + AgentId : Guid?
  + TaskId : Guid?
  + Details : JsonDocument?
}

class AncestorChain <<value object>> {
  + Agents : IReadOnlyList<Guid>
  + Contains(agentId) : bool
  + Append(agentId) : AncestorChain
  + {static} StartWith(callerAgentId) : AncestorChain
}

class AersError <<value object>> {
  + Code : string
  + Message : string
  + Details : JsonDocument?
  + Retryable : bool?
}

Agent "1" *-- "1..*" AgentCapability
Agent "1" o-- "0..*" AgentCredential
AgentTask "1" *-- "0..*" TaskStatusHistory
AgentTask "0..*" --> "1" Agent : caller
AgentTask "0..*" --> "1" Agent : target
AgentTask "0..1" --> "0..*" AgentTask : parent
AgentTask *-- AncestorChain
AgentTask *-- AersError
AgentTask --> TaskStatus
@enduml
```

---

## D5. Application Class Diagram ⭐ (interface, service, DTO)

Diagram này là contract của tầng Application cho Tuần 4 (đóng băng tại task T4-01). Interface thuộc Tuần 6–8 chỉ thể hiện ranh giới và chưa có đặc tả chi tiết.

```plantuml
@startuml AERS_Application
title AERS.Application - Interfaces, Services, DTOs
hide empty members

package "Registry (Tuần 4)" {
  interface IAgentRegistry {
    + RegisterAsync(RegisterAgentCommand) : RegisterAgentResult
    + HeartbeatAsync(agentId, AuthenticatedAgentContext) : void
    + DeregisterAsync(agentId, AuthenticatedAgentContext) : void
    + GetAsync(agentId, AuthenticatedAgentContext) : AgentDetailDto
  }
  note right of IAgentRegistry
    Tuần 5 bổ sung: ListAsync(AgentQuery),
    GetCapabilitiesAsync(agentId).
  end note

  class AgentRegistryService
  IAgentRegistry <|.. AgentRegistryService

  interface IAgentRepository {
    + AddAsync(Agent, AgentCredential) : void
    + FindAsync(Guid) : Agent?
    + SaveChangesAsync() : void
  }
  note bottom of IAgentRepository
    SaveChangesAsync commit trong một transaction:
    Agent + AgentCapability[] + AgentCredential + AuditEvent.
  end note

  interface ICredentialRepository {
    + FindActiveByHashAsync(apiKeyHash) : AgentCredential?
    + RevokeAllAsync(agentId, now) : void
  }

  interface IApiKeyService {
    + Generate() : IssuedApiKey
    + Hash(rawKey) : string
  }

  interface IRegistrationKeyValidator {
    + IsValid(presentedKey) : bool
  }

  interface IAgentPresenceStore {
    + MarkAliveAsync(agentId, now, ttl) : void
    + GetAsync(agentId) : AgentPresence
    + RemoveAsync(agentId) : void
  }

  interface IAgentCardValidator {
    + ValidateAndParseRegistration(JsonElement) : AgentCardSubmissionDto
    + ValidateHeartbeatBody(JsonElement?) : void
    + ValidateDeregistrationBody(JsonElement?) : void
  }
  note bottom of IAgentCardValidator
    Validate trên wire JSON (do Api gọi) để phát hiện
    trường lạ như agentId. Chi tiết: Protocol Spec mục 1.2.1.
  end note

  interface IAgentIdGenerator {
    + NewId() : Guid
  }

  interface IAuditWriter {
    + Add(eventType, agentId?, taskId?, details?) : void
  }

  AgentRegistryService --> IAgentRepository
  AgentRegistryService --> ICredentialRepository
  AgentRegistryService --> IApiKeyService
  AgentRegistryService --> IAgentPresenceStore
  AgentRegistryService --> IAgentIdGenerator
  AgentRegistryService --> IAuditWriter
}

package "Task / Policy / Relay (Tuần 6-8)" {
  interface ITaskManager {
    + CreateTaskAsync(callerId, CreateTaskRequestDto) : AgentTask
    + UpdateStatusAsync(taskId, status, result?) : void
    + GetTaskAsync(taskId, requesterId) : TaskQueryResultDto
  }
  interface IPolicyEngine {
    + Authorize(PolicyContext) : PolicyResult
    + CheckRateLimit(callerId) : RateLimitDecision
    + CheckRecursionDepth(rootTaskId, depth) : bool
    + CheckCycle(CycleCheckContext) : bool
  }
  interface IMessageRelay {
    + SendInvocation(targetAgentId, TaskInvocationDto) : void
    + PushStatusEvent(callerAgentId, TaskStatusEventDto) : void
  }
  interface IRuntimeStateStore {
    + SetAsync(TaskRuntimeState, ttl) : void
    + GetAsync(taskId) : TaskRuntimeState?
  }
  interface IConnectionRegistry {
    + Bind(agentId, connectionId, instanceId) : void
    + Unbind(connectionId) : void
    + Resolve(agentId) : ConnectionInfo?
  }
  interface IRateLimiter {
    + TryAcquire(callerId) : RateLimitDecision
  }
}

package "DTOs (Application)" {
  class RegisterAgentCommand {
    + Card : AgentCardSubmissionDto
    + AuthenticatedPrincipal : AuthenticatedAgentContext
  }
  class RegisterAgentResult {
    + AgentId : Guid
    + ApiKey : string  <<hiện đúng 1 lần>>
  }
  class AgentCardSubmissionDto {
    + Name : string
    + Domain : string  <<đã chuẩn hoá>>
    + Description : string
    + OwnerRef : string?
    + Capabilities : List<CapabilityDto>
  }
  class CapabilityDto {
    + Name : string
    + Description : string
    + InputSchema : JsonElement
    + OutputSchema : JsonElement
  }
  class AgentDetailDto {
    + AgentId : Guid
    + Card : AgentCardSubmissionDto
    + Status : AgentStatus
    + LastHeartbeatAt : DateTimeOffset?
  }
  class AgentPresence {
    + IsOnline : bool
    + LastHeartbeatAt : DateTimeOffset?
  }
  class IssuedApiKey {
    + RawKey : string
    + Hash : string
  }
  class AuthenticatedAgentContext {
    + AgentId : Guid?  <<null với registration key>>
    + Domain : string?
    + Scheme : JWT | API_KEY
  }
  class AersPolicyOptions {
    + MaxRecursionDepth = 5
    + DefaultTaskTimeoutSeconds = 300
    + RateLimit : {WindowSeconds=60, MaxTasksPerWindow=100}
    + Heartbeat : {IntervalSeconds=15, OfflineThresholdSeconds=45}
    + CycleDetection.Granularity = AgentOnly
    + DomainPolicy : {Mode=DenyByDefault, CrossDomainRules}
  }
  class AuthOptions {
    + RegistrationKeys : List<string>
  }
  class RegistrationLimitsOptions {
    + MaxNameLength = 200
    + MaxCapabilities = 50
    + MaxSchemaBytes = 65536
    + MaxBodyBytes = 1048576
  }
}

class "AersException" as Ex {
  + Error : AersError
  + HttpStatus : int
}
note right of Ex
  Application ném AersException(code); Api middleware ánh xạ sang
  HTTP status và body AersError (Protocol Spec mục 12.3).
end note
@enduml
```

### Ánh xạ Wire → Application → Persistence (Tuần 4)

| Wire (JSON schema) | Application | Persistence |
|---|---|---|
| `AgentRegistrationRequest{agentCard: AgentCardSubmission}` | `AgentCardSubmissionDto` → `RegisterAgentCommand` | `Agent` + `AgentCapability[]` + `AgentCredential` |
| `AgentRegistrationResponse{agentId, apiKey}` | `RegisterAgentResult` | `agent_credentials.api_key_hash` (chỉ lưu giá trị băm) |
| `AgentDetailResponse{agentCard, runtimeStatus}` | `AgentDetailDto` | `Agent` + `AgentCapability[]` (+ Redis presence) |
| `AgentRuntimeStatus` | `AgentPresence` → `AgentDetailDto.Status/LastHeartbeatAt` | Redis `aers:agent:{id}:alive`, `aers:agent:{id}:lastbeat` |
| `HeartbeatRequest` | không có DTO (chỉ `agentId` từ credential) | Redis |

---

## D6. ERD — PostgreSQL ⭐

```plantuml
@startuml AERS_ERD
title AERS - ERD (PostgreSQL / EF Core)
hide circle
skinparam linetype ortho

entity "agents" as agents {
  * id : uuid <<PK>>  -- UUID v4 do AERS sinh
  --
  * name : varchar(200)
  * domain : varchar(100) <<IX>>
  * description : text
  owner_ref : varchar(200) NULL  -- extension point cho Tenant
  * registered_at : timestamptz
  deregistered_at : timestamptz NULL  -- soft delete
}

entity "agent_capabilities" as caps {
  * id : uuid <<PK>>
  --
  * agent_id : uuid <<FK>>
  * name : varchar(200)
  * description : text
  * input_schema : jsonb
  * output_schema : jsonb
  --
  UNIQUE (agent_id, name)
  IX (name)
}

entity "agent_credentials" as creds {
  * id : uuid <<PK>>
  --
  * agent_id : uuid <<FK>>
  * api_key_hash : varchar(128) <<UQ>>
  * created_at : timestamptz
  revoked_at : timestamptz NULL
}

entity "tasks" as tasks {
  * id : uuid <<PK>>
  --
  * caller_agent_id : uuid <<FK>>
  * target_agent_id : uuid <<FK>>
  * capability_name : varchar(200)
  * status : smallint  -- 0 submitted,1 working,2 completed,3 failed
  * input : jsonb
  output : jsonb NULL
  parent_task_id : uuid <<FK self>> NULL
  * root_task_id : uuid <<IX>>
  * depth : int
  * ancestor_chain : jsonb  -- mảng agentId, server-side only
  * deadline_at : timestamptz
  * created_at : timestamptz
  started_at : timestamptz NULL
  completed_at : timestamptz NULL
  acknowledged_at : timestamptz NULL  -- ADR-W7-02, caller đã ack kết quả
  error : jsonb NULL  -- {code,message,details,retryable}
  --
  IX (status, deadline_at)
  IX (caller_agent_id), IX (target_agent_id)
  IX ix_tasks_caller_unacknowledged (caller_agent_id, completed_at) WHERE status IN (2, 3) AND acknowledged_at IS NULL
}

entity "task_status_history" as hist {
  * id : bigserial <<PK>>
  --
  * task_id : uuid <<FK>>
  from_status : smallint NULL
  * to_status : smallint
  * changed_at : timestamptz
}

entity "audit_events" as audit {
  * id : bigserial <<PK>>
  --
  * event_type : varchar(100) <<IX>>
  * occurred_at : timestamptz <<IX>>
  agent_id : uuid NULL  -- không FK, giữ log khi Agent bị xoá
  task_id : uuid NULL
  details : jsonb NULL
}

agents ||--|{ caps
agents ||--o{ creds
agents ||--o{ tasks : caller
agents ||--o{ tasks : target
tasks  ||--o{ tasks : parent
tasks  ||--o{ hist
@enduml
```

Ghi chú implement EF Core:
- `status` lưu bằng `HasConversion<short>()`; `AncestorChain`/`Error` map bằng `OwnsOne(...).ToJson()` hoặc `HasConversion` sang jsonb (Npgsql).
- `audit_events.agent_id` / `task_id` **không** đặt FK có chủ đích: audit phải sống lâu hơn Agent/Task.
- Không có bảng `domains` / `tenants`: `domain` là cột string có index (đúng quyết định MVP). `owner_ref` nullable giữ chỗ cho Tenant.
- Online/offline **không** có cột: suy ra từ Redis (D7).

---

## D7. Redis Data Model ⭐

```plantuml
@startuml AERS_Redis
title AERS - Redis Keys
hide empty members

class "aers:agent:{agentId}:alive" <<String>> {
  value = "1"
  TTL = 45s (Heartbeat.OfflineThresholdSeconds)
  ghi: SET ... EX 45 (mỗi heartbeat, mỗi 15s)
  đọc: EXISTS -> online/offline
}

class "aers:agent:{agentId}:lastbeat" <<String>> {
  value = ISO-8601 timestamp
  TTL = không (hoặc dài)
  mục đích: trả lastHeartbeatAt cho AgentRuntimeStatus
}
note right
  ĐỀ XUẤT: spec có field lastHeartbeatAt
  nhưng chưa nói lưu ở đâu.
end note

class "aers:task:{taskId}" <<Hash>> {
  taskId, status, updatedAt,
  connectionId, instanceId, deadlineAt
  TTL = deadlineAt - now + 60s (tạo)
        -> 60s khi completed/failed
}

class "aers:ratelimit:{callerAgentId}:{windowStart}" <<String counter>> {
  INCR + EXPIRE 60s
  limit = 100 task.create / window
}

class "aers:conn:{agentId}" <<Hash>> {
  connectionId, instanceId
  mục đích: Relay biết Agent đang nối instance nào
}
note right
  ĐỀ XUẤT (spec chưa chốt "connection mapping").
  SignalR backplane đã lo route theo connectionId,
  nhưng cần map agentId -> connectionId.
end note

note right of "aers:task:{taskId}"
  R3 (ADR-W7-01): chỉ ghi. Ghi sau commit thắng compare-and-set,
  HSET + PEXPIRE trong một Lua script. TTL = deadline - now + 60s
  (chưa terminal) hoặc 60s (terminal).
end note

class "aers:disconnected" <<Sorted set>> {
  member = agentId, score = unix ms lúc ngắt kết nối
  không TTL; worker xoá khi Agent nối lại hoặc hết Task working
  ADR-W7-03: ân hạn TargetDisconnectGraceSeconds = 30
}

class "aers:agent:revoked" <<Pub/Sub channel>> {
  message = agentId (khi deregister)
  mọi instance subscribe, instance giữ kết nối thì abort
  ADR-W8-03; không lưu tin
}
@enduml
```

---

## D8. Sequence — Register Agent ⭐

```plantuml
@startuml AERS_Register
title Register Agent (POST /agents/register)

actor "Agent Developer\n(client)" as C
participant "AuthMiddleware" as Auth
participant "RegistrationController" as Ctl
participant "AgentCardValidator\n(JsonSchema.Net)" as Val
participant "AgentRegistryService" as Svc
participant "AgentRepository\n(EF Core / PG)" as Repo
participant "PresenceStore\n(Redis)" as Pres
participant "AuditWriter" as Aud

C -> Auth : POST /agents/register\nX-Registration-Key: <key do Operator cấp>
alt registration key không hợp lệ
  Auth --> C : 401 AUTHENTICATION_FAILED
end
Auth -> Ctl : AuthenticatedAgentContext(agentId=null, scheme=API_KEY)
Ctl -> Val : ValidateAndParseRegistration(raw JsonElement)
alt sai schema / có "agentId" / schema con không hợp lệ / trùng capability.name
  Val --> Ctl : throw AersException(VALIDATION_ERROR, details.errors)
  Ctl --> C : 400 { code: VALIDATION_ERROR, details }
end
Val --> Ctl : AgentCardSubmissionDto
Ctl -> Svc : RegisterAsync(command)
Svc -> Svc : agentId = Guid.NewGuid() (UUID v4)
Svc -> Svc : apiKey = random 256-bit (CSPRNG)\nhash = SHA-256(apiKey)
Svc -> Repo : BEGIN TRANSACTION\nadd Agent + AgentCapability[] + AgentCredential(hash)
Repo --> Svc : committed
Svc -> Pres : MarkAlive(agentId, 45s)
Svc -> Aud : agent.registered { agentId }
Svc --> Ctl : RegisterAgentResult(agentId, apiKey)
Ctl --> C : 201 Created { agentId, apiKey }
note right of C
  Agent lưu apiKey (không xem lại được). Mọi request sau
  (heartbeat, discovery, task) dùng X-Api-Key: <apiKey>;
  AERS resolve agentId từ key, KHÔNG tin agentId client gửi.
  Agent dùng agentId này cho heartbeat/discovery.
  Agent mới xuất hiện trong GET /agents ngay,
  KHÔNG cần redeploy AERS.
end note
@enduml
```

---

## D9. Sequence — Heartbeat, Online Status, Deregister ⭐

```plantuml
@startuml AERS_Heartbeat_Deregister
title Heartbeat / Status / Deregister

actor Agent
participant "AuthMiddleware" as Auth
participant "AgentsController" as Ctl
participant "AgentRegistryService" as Svc
participant "AgentRepository" as Repo
participant "PresenceStore (Redis)" as Pres
participant "AuditWriter" as Aud

== Heartbeat mỗi 15s ==
Agent -> Auth : POST /agents/{id}/heartbeat
Auth -> Ctl : AuthenticatedAgentContext(agentId=X)
Ctl -> Svc : HeartbeatAsync(id, X)
alt id != X
  Svc --> Agent : 403 (không được heartbeat thay Agent khác)
end
Svc -> Repo : Find(id) (cache được)
alt không tồn tại hoặc đã deregister
  Svc --> Agent : 404 AGENT_NOT_FOUND
end
Svc -> Pres : SET aers:agent:{id}:alive "1" EX 45\nSET aers:agent:{id}:lastbeat <now>
Svc --> Agent : 204 No Content

== Đọc trạng thái (passive, không worker) ==
Agent -> Ctl : GET /agents/{id}
Ctl -> Svc : GetAsync(id)
Svc -> Repo : Find(id)
Svc -> Pres : EXISTS aers:agent:{id}:alive
Pres --> Svc : true | false
Svc --> Agent : 200 AgentCard (+ status online|offline)

== Deregister ==
Agent -> Auth : DELETE /agents/{id}  (body rỗng)
Auth -> Ctl : AuthenticatedAgentContext(agentId=X)
Ctl -> Svc : DeregisterAsync(id, X)
alt id != X (và X không phải Operator)
  Svc --> Agent : 403
end
Svc -> Repo : set deregistered_at = now (soft delete)\nrevoke credential
Svc -> Pres : DEL aers:agent:{id}:alive
Svc -> Aud : agent.deregistered { agentId }
Svc --> Agent : 204 No Content
note over Svc
  Task đang chạy của Agent này: hệ thống để
  Task chạy tới terminal/timeout (không huỷ cưỡng bức ở MVP).
end note
@enduml
```

---

## D10. Sequence — Discovery

```plantuml
@startuml AERS_Discovery
title Capability Discovery

actor "Agent A" as A
participant "AuthMiddleware" as Auth
participant "DiscoveryController" as Ctl
participant "AgentRegistryService" as Svc
participant "AgentRepository" as Repo
participant "PresenceStore" as Pres

A -> Auth : GET /agents?domain=jobhunt&capability=analyze_cv
Auth -> Ctl : AuthenticatedAgentContext(A, domain=jobhunt)
Ctl -> Svc : ListAsync(query)
note over Svc
  Mặc định scope = domain của caller.
  Cross-domain chỉ trả về khi DomainPolicy cho phép.
end note
Svc -> Repo : Query(domain, capability, active only)
Repo --> Svc : Agents[]
loop từng Agent
  Svc -> Pres : EXISTS alive
end
Svc --> A : 200 AgentDiscoveryResult { agents: AgentCard[] }

A -> Ctl : GET /agents/{id}/capabilities
Ctl -> Svc : GetCapabilitiesAsync(id)
Svc --> A : 200 [ { name, description, inputSchema, outputSchema } ]
@enduml
```

---

## D11. Sequence — Sync Task A → B (đủ pipeline, SignalR)

```plantuml
@startuml AERS_SyncTask
title Create Task A -> B (sync, qua SignalR)

participant "Agent A" as A
participant "AgentHub" as Hub
participant "TaskManager" as TM
participant "PolicyEngine" as Pol
participant "TaskRepo (PG)" as DB
participant "RuntimeState (Redis)" as RS
participant "MessageRelay" as Rel
participant "Agent B" as B
participant "AuditWriter" as Aud

A -> Hub : Envelope{ messageType: task.create,\ncorrelationId?, payload: CreateTaskRequest }
Hub -> TM : CreateTask(callerId = từ auth context, request)
TM -> Pol : Authorize + RateLimit + Depth + Cycle (xem D14)
note right of TM
  Khi Policy từ chối: không tạo Task, chỉ trả lỗi và ghi audit.
  Đây là đề xuất OD-3, chưa chốt (Spec mục 3.1 hiện cho phép submitted -> failed).
end note
alt bị từ chối
  Pol --> TM : PolicyResult(allowed=false, error)
  TM -> Aud : policy.rejected { errorCode }
  TM --> Hub : error
  Hub --> A : Envelope{ task.result, status=failed, error }
end
Pol --> TM : allowed
TM -> TM : taskId = new, rootTaskId = taskId, depth = 0,\nancestorChain = [A], deadlineAt = now + 300s
TM -> DB : insert Task(submitted) + status_history
TM -> RS : HSET aers:task:{id}, TTL = deadline-now+60s
TM -> Aud : task.created
TM -> Rel : SendInvocation(B, TaskInvocation)
alt B offline / không có connection
  Rel --> TM : unavailable
  TM -> DB : Task -> failed (TARGET_UNAVAILABLE)
  TM --> A : task.result failed TARGET_UNAVAILABLE
end
Rel -> B : Envelope{ task.invoke, TaskInvocation }\n(không có ancestorChain)
B -> Hub : Envelope{ task.status_event, working }
Hub -> TM : UpdateStatus(working)
TM -> DB : Task -> working, history
TM -> Aud : task.status_changed
B -> Hub : Envelope{ task.result, completed, output }
Hub -> TM : UpdateStatus(completed, output)
TM -> DB : Task -> completed
TM -> RS : TTL -> 60s
TM -> Aud : task.status_changed
TM -> Rel : deliver result to A
Rel -> A : Envelope{ task.result, TaskResult }
@enduml
```

---

## D12. Sequence — Async + đệ quy + cycle

> Công thức đã chốt: `ancestorChain_new = ancestorChain_parent + [targetAgentId của parent]`.
> Công thức xác định chuỗi của Task con; chuỗi của root Task được khởi tạo là `[callerAgentId]`. Nếu root Task bắt đầu với chuỗi rỗng, Agent gọi gốc không bao giờ nằm trong chuỗi và vòng A→B→C→A không bị phát hiện. Xem `Protocol-Specification-v3.md` mục 4 và quyết định D-07.

```plantuml
@startuml AERS_Recursive_Fixed
title Async + Recursive A -> B -> C, và chặn C -> A

participant "Agent A" as A
participant "Agent B" as B
participant "Agent C" as C
participant "AERS\n(TaskManager + Policy + Relay)" as S

A -> S : task.create T1 (parentTaskId=null, target=B)
S -> S : T1: root=T1, depth=0, chain=[A]
S -> B : task.invoke T1
B -> S : task.status_event T1=working
S -> A : push T1=working

B -> S : task.create T2 (parentTaskId=T1, target=C)
S -> S : T2: root=T1, depth=1,\nchain = chain(T1) + [target(T1)=B] = [A,B]
S -> S : Policy: depth 1 <= 5 OK; C ∉ [A,B] OK
S -> C : task.invoke T2
C -> S : task.create T3 (parentTaskId=T2, target=A)
S -> S : T3: depth=2,\nchain = chain(T2) + [target(T2)=C] = [A,B,C]
S -> S : CheckCycle: A ∈ [A,B,C] -> REJECT
S --> C : error CYCLE_DETECTED
note right of S
  Audit: policy.rejected { errorCode: CYCLE_DETECTED }
  Chain là server-side state, caller chỉ gửi parentTaskId.
end note

C -> S : task.result T2 completed
S -> B : push T2 completed
B -> S : task.result T1 completed
S -> A : push T1 completed
@enduml
```

---

## D13. Sequence — Timeout worker + Reconnect

```plantuml
@startuml AERS_Timeout_Reconnect
title Timeout Scheduler & Agent reconnect

participant "TimeoutScheduler\n(BackgroundService)" as W
participant "TaskRepo (PG)" as DB
participant "TaskManager" as TM
participant "MessageRelay" as Rel
actor "Agent A (caller)" as A

== Timeout ==
loop mỗi N giây (mọi instance chạy; dùng khoá/UPDATE có điều kiện để 1 instance thắng)
  W -> DB : SELECT tasks WHERE status IN (submitted,working)\nAND deadline_at < now
  loop mỗi task quá hạn
    W -> TM : FailTask(taskId, TASK_TIMEOUT)
    TM -> DB : UPDATE ... WHERE status NOT terminal (optimistic)
    TM -> Rel : push task.result failed -> caller
  end
end

== Target mất kết nối (ADR-W7-03) ==
note over W
  Dấu thời điểm ngắt ở Redis (aers:disconnected). Sau 30 giây không nối lại,
  worker gọi FailTasksOfDisconnectedTargetAsync (compare-and-set working -> failed,
  TARGET_UNAVAILABLE) rồi push cho caller. Xem Sequence Diagrams mục 9.
end note

== Reconnect (ADR-W7-02) ==
A -> Rel : (mất kết nối SignalR)
note over A : push của T1 bị lỡ
A -> A : reconnect + gửi lại auth
note over A : AERS gửi lại task.result chưa ack (acknowledged_at IS NULL);\nA xử lý rồi gửi task.ack
A -> TM : GET /tasks/{T1}
TM -> TM : đọc Redis, fallback PG nếu TTL đã hết
TM -> TM : kiểm tra A ∈ {caller, target}
TM --> A : 200 TaskQueryResult (status, output)
@enduml
```

---

## D14. Activity — Policy Pipeline

Thứ tự theo `Protocol-Specification-v3.md` mục 9.4 (R3, ADR-W8-01). Mọi nhánh từ chối từ bước rate limit trở đi ghi audit `policy.rejected { errorCode }` và trả `AersError` cho caller; không tạo Task.

```plantuml
@startuml AERS_PolicyPipeline
title Policy Check Pipeline (R3)
start
:Nhận task.create;
if (Authenticated?) then (không)
  :Từ chối: AUTHENTICATION_FAILED;
  detach
endif
if (Payload đúng hình dạng?) then (không)
  :Từ chối: VALIDATION_ERROR (không tính quota);
  detach
endif
if (Caller tồn tại, chưa deregister?) then (không)
  :Từ chối: AUTHENTICATION_FAILED (không tính quota);
  detach
endif
if (Rate limit OK? 100 task.create / 60s) then (không)
  :Từ chối: RATE_LIMITED\n{limit, remaining, resetAt}, retryable;
  detach
endif
note right
  Redis lỗi: fail-closed,
  INTERNAL_ERROR retryable.
end note
if (Target tồn tại, chưa deregister?) then (không)
  :Từ chối: AGENT_NOT_FOUND;
  detach
endif
if (parentTaskId hợp lệ?) then (không)
  :Từ chối (quy tắc parent);
  detach
endif
if (Cùng domain hoặc cặp domain nằm trong allowlist?) then (không)
  :Từ chối: DOMAIN_FORBIDDEN;
  detach
endif
if (Target có capability?) then (không)
  :Từ chối: CAPABILITY_NOT_FOUND;
  detach
endif
if (input hợp lệ theo inputSchema?) then (không)
  :Từ chối: VALIDATION_ERROR;
  detach
endif
if (depth <= MaxRecursionDepth = 5?) then (không)
  :Từ chối: DEPTH_EXCEEDED\n{maxDepth, depth};
  detach
endif
if (target không thuộc ancestorChain?) then (không)
  :Từ chối: CYCLE_DETECTED\n{rootTaskId};
  detach
endif
:Tạo Task (submitted), gán deadline = min(now + 300s, deadline cha),\ngắn orphanSuspect nếu cần, ghi runtime state, relay;
stop
@enduml
```

---

## D15. State Diagrams

```plantuml
@startuml AERS_States
title Task Lifecycle & Agent Presence

state "Task" as T {
  [*] --> submitted : CreateTask (Policy pass)
  submitted --> working : Target nhận & báo working
  working --> completed : task.result OK
  working --> failed : task.result lỗi / TASK_TIMEOUT
  submitted --> failed : TARGET_UNAVAILABLE / timeout
  completed --> [*]
  failed --> [*]
}
note bottom of T
  Đề xuất OD-3 (chưa chốt): Policy reject xảy ra TRƯỚC khi Task được tạo;
  lỗi trả thẳng cho caller + audit policy.rejected.
  Spec mục 3.1 hiện cho phép submitted -> failed khi Policy từ chối.
end note
@enduml
```

```plantuml
@startuml AERS_AgentState
title Agent Registration & Presence

[*] --> Registered : POST /agents/register (201)
state Registered {
  [*] --> Online : MarkAlive (lúc register)
  Online --> Offline : 45s không heartbeat\n(Redis key hết hạn)
  Offline --> Online : heartbeat
}
Registered --> Deregistered : DELETE /agents/{id}
Deregistered --> [*]
@enduml
```

---

## D16. Deployment Diagram

```plantuml
@startuml AERS_Deployment
title AERS - Deployment (Docker Compose)

node "Clients" {
  [Dummy / Reference Agents]
}
node "docker-compose" {
  node "load-balancer\n(Nginx/YARP, sticky không bắt buộc\nvì có Redis backplane)" as LB
  node "aers-1" as I1
  node "aers-2" as I2
  database "redis" as R
  database "postgres" as PG
}

[Dummy / Reference Agents] --> LB : HTTPS + WebSocket (SignalR)
LB --> I1
LB --> I2
I1 --> R : state + backplane + rate limit
I2 --> R
I1 --> PG : EF Core
I2 --> PG
note bottom of PG
  Migration chạy ở 1 nơi (job/entrypoint),
  không để 2 instance migrate đồng thời.
end note
@enduml
```

---

## Tham chiếu quyết định thiết kế

Các quyết định ảnh hưởng đến diagram trong tài liệu này được quản lý tại `05-decision-log-v3.md`.

| Mã | Nội dung | Diagram liên quan | Trạng thái |
|---|---|---|---|
| D-04 | Credential: registration key + Agent API key (hash SHA-256) | D4, D5, D6, D8, D9 | Approved |
| D-05 | Deregistration xoá mềm | D4, D6, D9, D15 | Approved |
| D-07 | Công thức `ancestorChain`; root = `[callerAgentId]` | D4, D12 | Approved (trường hợp cơ sở: Adopted R2) |
| OD-3 | Policy từ chối có tạo Task `failed` hay không | D11, D15 | Open |
| OD-5 | Bước kiểm tra `input` theo `inputSchema` trong Policy | D14 | Adopted R2 |
| ADR-W7-01 … W9-02 | Chín quyết định Tuần 7 (runtime state chỉ ghi, `task.ack`, ân hạn mất kết nối, rate limit sớm, thu hồi kết nối, ...) | D4, D6, D7, D9, D13, D14 | Accepted |
| OD-6 | Lưu `deadline_at`, `ancestor_chain` ở PostgreSQL | D4, D6, D13 | Adopted R2 |

---

## Lịch sử sửa đổi

| Revision | Nội dung |
|---|---|
| R1 | Bản khởi tạo (16 diagram). |
| R2 | Đồng bộ trước Tuần 4: D5 cập nhật theo contract đã đóng băng (tách `IApiKeyService`, `IRegistrationKeyValidator`, `ICredentialRepository`; validator nhận wire JSON; `RegisterAgentResult` có `apiKey`); D8 chuyển bước validate sang trước service; D12 làm rõ trường hợp cơ sở của `ancestorChain`; D14 viết lại theo cú pháp `detach` và thêm bước validate `input`; thay phần quyết định cuối tài liệu bằng tham chiếu Decision Log. |
| R3 | Đồng bộ Tuần 7: D4 và D6 thêm `acknowledged_at` và index một phần; D7 thêm `aers:disconnected` và kênh `aers:agent:revoked`; D13 ghi chú ân hạn và gửi lại; D14 viết lại theo thứ tự Policy mới. |

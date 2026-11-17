# AERS — Protocol Specification v1.0

**Đề tài:** Phát triển hệ thống backend cho Agent-to-Agent Runtime (AERS)
**Trạng thái:** Final — Deliverable chính thức Tuần 2 (Thiết kế Protocol); Revision R3 (đồng bộ Tuần 7; R2 là đồng bộ trước Tuần 4; xem mục “Lịch sử sửa đổi” ở cuối tài liệu)
**Phiên bản protocol:** 1.0. Hậu tố `-v3` trong tên tệp là chỉ số của bộ tài liệu, không phải phiên bản protocol.
**Chuẩn schema:** JSON Schema Draft 2020-12
**Validator:** `JsonSchema.Net`

Toàn bộ dữ liệu trong hệ thống tuân theo 3 lớp mô hình, không đồng nhất với nhau:

```text
Wire Protocol (envelope trao đổi qua SignalR/REST)
        ↓
Application DTO (dùng nội bộ trong service)
        ↓
Persistence Entity (PostgreSQL/EF Core)
```

---

## 1. Agent Card & Registration

### 1.1. Capability

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/capability.schema.json",
  "title": "AERS Capability",
  "type": "object",
  "required": ["name", "description", "inputSchema", "outputSchema"],
  "properties": {
    "name": { "type": "string", "description": "Tên capability được dùng khi tạo Task." },
    "description": { "type": "string" },
    "inputSchema": { "type": "object", "description": "JSON Schema mô tả input hợp lệ của capability." },
    "outputSchema": { "type": "object", "description": "JSON Schema mô tả output của capability." }
  },
  "additionalProperties": false
}
```

### 1.2. AgentCardSubmission — payload đăng ký

Gửi lên khi `POST /agents/register`. Không được phép chứa `agentId` — nếu client gửi kèm, request bị
từ chối với `VALIDATION_ERROR`.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/agent-card-submission.schema.json",
  "title": "AERS Agent Card Submission",
  "type": "object",
  "required": ["name", "domain", "description", "capabilities"],
  "properties": {
    "name": { "type": "string", "description": "Tên hiển thị của Agent." },
    "domain": { "type": "string", "description": "Domain/namespace mà Agent thuộc về." },
    "description": { "type": "string" },
    "capabilities": {
      "type": "array",
      "items": { "$ref": "capability.schema.json" }
    },
    "ownerRef": {
      "type": ["string", "null"],
      "description": "Điểm mở rộng cho owner/tenant trong tương lai. Không mang ý nghĩa Tenant trong phạm vi hiện tại."
    }
  },
  "additionalProperties": false
}
```

#### 1.2.1. Quy tắc kiểm tra bổ sung đối với AgentCardSubmission

Ngoài việc khớp schema ở mục 1.2, AERS áp dụng các quy tắc sau trước khi chấp nhận đăng ký. Các giá trị giới hạn được cấu hình qua section `Aers:RegistrationLimits` (mục 9.3).

| Trường | Quy tắc |
|---|---|
| `name` | 1–200 ký tự sau khi loại bỏ khoảng trắng đầu/cuối |
| `domain` | Được chuẩn hoá (trim, chữ thường) trước khi lưu; phải khớp `^[a-z0-9][a-z0-9-]{0,63}$` |
| `capabilities` | Từ 1 đến 50 phần tử |
| `capabilities[].name` | Khớp `^[A-Za-z][A-Za-z0-9_.-]{0,99}$`; duy nhất trong phạm vi một Agent (so sánh ordinal, phân biệt chữ hoa/thường) |
| `capabilities[].inputSchema`, `outputSchema` | Phải là JSON Schema hợp lệ theo Draft 2020-12 (kiểm tra với metaschema); kích thước tối đa 64 KiB mỗi schema |
| Request body | Kích thước tối đa 1 MiB |

Mọi vi phạm trả về `400 VALIDATION_ERROR` với `details.errors` là mảng `{ "path": "<JSON Pointer>", "message": "<mô tả>" }`. AERS thu thập toàn bộ lỗi phát hiện được thay vì dừng ở lỗi đầu tiên. Trường `agentId` xuất hiện trong payload đăng ký luôn bị coi là vi phạm, với thông điệp nêu rõ `agentId` do AERS cấp.

### 1.3. AgentCard — dùng cho discovery/trả về

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/agent-card.schema.json",
  "title": "AERS Agent Card",
  "type": "object",
  "required": ["agentId", "name", "domain", "description", "capabilities"],
  "properties": {
    "agentId": {
      "type": "string",
      "description": "UUID v4, do AERS tự sinh khi registration. Globally unique trong phạm vi một AERS instance/cluster, kể cả khác domain. Agent Developer không tự đặt được."
    },
    "name": { "type": "string" },
    "domain": { "type": "string" },
    "description": { "type": "string" },
    "capabilities": {
      "type": "array",
      "items": { "$ref": "capability.schema.json" }
    },
    "ownerRef": { "type": ["string", "null"] }
  },
  "additionalProperties": false
}
```

### 1.4. Registration Request / Response

```json
{
  "$id": "https://aers.local/schemas/agent-registration-request.schema.json",
  "type": "object",
  "required": ["agentCard"],
  "properties": { "agentCard": { "$ref": "agent-card-submission.schema.json" } },
  "additionalProperties": false
}
```

```json
{
  "$id": "https://aers.local/schemas/agent-registration-response.schema.json",
  "type": "object",
  "required": ["agentId", "apiKey"],
  "properties": {
    "agentId": { "type": "string", "description": "UUID v4 do AERS cấp." },
    "apiKey": { "type": "string", "description": "Agent API key. Chỉ được trả về duy nhất một lần tại thời điểm đăng ký." }
  },
  "additionalProperties": false
}
```

**Xác thực:** request mang header `X-Registration-Key` (mục 8.3). Credential này chỉ có hiệu lực cho endpoint đăng ký.

**Kết quả thành công:** `201 Created`; header `Location: /agents/{agentId}`; header `Cache-Control: no-store` (response chứa secret).

**Ví dụ request:**

```json
{
  "agentCard": {
    "name": "CV Analysis Agent",
    "domain": "jobhunt",
    "description": "Analyze a CV against a job",
    "capabilities": [
      {
        "name": "analyze_cv",
        "description": "Analyze a CV against a job",
        "inputSchema": { "type": "object", "properties": { "cvText": { "type": "string" } }, "required": ["cvText"] },
        "outputSchema": { "type": "object", "properties": { "score": { "type": "number" } } }
      }
    ],
    "ownerRef": null
  }
}
```

**Ví dụ response:**

```json
{
  "agentId": "3f2b8c1e-5a47-4d9e-9b1a-6c0e2d7f4a85",
  "apiKey": "aers_<base64url, 43 ký tự>"
}
```

**Hành vi sau khi đăng ký thành công:**

- Agent, các capability và credential được lưu trong một transaction duy nhất; sự kiện audit `agent.registered` được ghi trong cùng transaction.
- Agent được đánh dấu online ngay lập tức (ghi key presence, mục 2.1). Việc ghi Redis được thực hiện sau khi cơ sở dữ liệu commit và theo nguyên tắc best-effort: lỗi Redis không làm thất bại đăng ký; heartbeat đầu tiên sẽ khôi phục trạng thái.
- `apiKey` không thể truy xuất lại; chỉ giá trị băm được lưu (mục 8.2). Agent làm mất `apiKey` phải đăng ký lại như một Agent mới.
- Hai lần đăng ký với cùng nội dung tạo ra hai Agent có `agentId` khác nhau; không có ràng buộc duy nhất theo tên.

**Lỗi:** `400 VALIDATION_ERROR` (mục 1.2.1), `401 AUTHENTICATION_FAILED`.

### 1.5. Discovery Result

```json
{
  "$id": "https://aers.local/schemas/agent-discovery-result.schema.json",
  "type": "object",
  "required": ["agents"],
  "properties": {
    "agents": { "type": "array", "items": { "$ref": "agent-card.schema.json" } }
  },
  "additionalProperties": false
}
```

### 1.6. Agent Runtime Status

```json
{
  "$id": "https://aers.local/schemas/agent-runtime-status.schema.json",
  "type": "object",
  "required": ["agentId", "status"],
  "properties": {
    "agentId": { "type": "string" },
    "status": { "type": "string", "enum": ["online", "offline"] },
    "lastHeartbeatAt": { "type": ["string", "null"], "format": "date-time" }
  },
  "additionalProperties": false
}
```

### 1.7. Agent Detail Response — `GET /agents/{id}`

`AgentCard` đặt `additionalProperties: false` và không chứa trạng thái runtime; do đó response của `GET /agents/{id}` là một schema riêng, kết hợp card với trạng thái runtime:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/agent-detail-response.schema.json",
  "title": "AERS Agent Detail Response",
  "type": "object",
  "required": ["agentCard", "runtimeStatus"],
  "properties": {
    "agentCard": { "$ref": "agent-card.schema.json" },
    "runtimeStatus": { "$ref": "agent-runtime-status.schema.json" }
  },
  "additionalProperties": false
}
```

`runtimeStatus.status` được suy ra từ Redis (mục 2.1); `runtimeStatus.lastHeartbeatAt` là `null` nếu chưa từng ghi nhận heartbeat.

### 1.8. Quy tắc truy cập khi đọc thông tin Agent

`GET /agents/{id}` yêu cầu `X-Api-Key`. Thứ tự đánh giá:

1. Agent không tồn tại hoặc đã deregister: `404 AGENT_NOT_FOUND`.
2. Cho phép nếu thoả một trong các điều kiện: (a) caller chính là Agent được truy vấn; (b) `callerDomain == targetDomain`; (c) cặp `(callerDomain, targetDomain)` nằm trong `DomainPolicy.CrossDomainRules`.
3. Các trường hợp còn lại: `403 DOMAIN_FORBIDDEN`.

Chính sách `DenyByDefault` (mục 9.3) được áp dụng; trong khi allowlist chưa được cấu hình, mọi truy vấn cross-domain bị từ chối.

---

## 2. Agent Lifecycle Operations

### 2.1. Heartbeat

```json
{
  "$id": "https://aers.local/schemas/heartbeat-request.schema.json",
  "type": "object",
  "properties": { "timestamp": { "type": ["string", "null"], "format": "date-time" } },
  "additionalProperties": false
}
```

- Endpoint: `POST /agents/{id}/heartbeat`; yêu cầu `X-Api-Key`.
- `agentId` trong route phải trùng `agentId` được suy ra từ credential; nếu không trùng: `403 AGENT_ACCESS_FORBIDDEN`.
- Agent không tồn tại hoặc đã deregister: `404 AGENT_NOT_FOUND`. Thành công: `204 No Content`.
- Request body là tuỳ chọn (rỗng, `{}` hoặc `{ "timestamp": ... }` đều hợp lệ). Giá trị `timestamp` chỉ được kiểm tra định dạng và **không** được sử dụng; AERS luôn dùng đồng hồ phía server.
- Interval gửi từ client: **15 giây**.
- Cơ chế xác định online/offline: **passive Redis TTL**, không dùng background worker riêng.

```text
Mỗi lần Agent gửi heartbeat (và ngay khi đăng ký thành công):
    Redis: SET aers:agent:{agentId}:alive "1" EX 45
    Redis: SET aers:agent:{agentId}:lastbeat <ISO-8601 UTC> EX 86400

Khi cần biết trạng thái online/offline:
    EXISTS aers:agent:{agentId}:alive
        → true  = online
        → false = offline (key đã hết hạn tự nhiên sau 45 giây không heartbeat)

lastHeartbeatAt:
    GET aers:agent:{agentId}:lastbeat  (null nếu key không tồn tại)
```

Tiền tố `aers:` được cấu hình qua `Aers:Redis:KeyPrefix`. Khi Redis không khả dụng, heartbeat và `GET /agents/{id}` trả `500 INTERNAL_ERROR`.

### 2.2. Deregistration

```json
{
  "$id": "https://aers.local/schemas/deregistration-request.schema.json",
  "type": "object",
  "maxProperties": 0
}
```

- Endpoint: `DELETE /agents/{id}`; yêu cầu `X-Api-Key`; body rỗng hoặc `{}`. Body chứa trường khác: `400 VALIDATION_ERROR`.
- `agentId` trong route phải trùng `agentId` của credential: `403 AGENT_ACCESS_FORBIDDEN` nếu không trùng. Thành công: `204 No Content`.
- Deregistration là **xoá mềm**: cột `deregistered_at` được đặt; bản ghi Agent không bị xoá khỏi cơ sở dữ liệu nhằm bảo toàn tính toàn vẹn tham chiếu với Task và Audit.

Hệ quả sau khi deregister:

1. Agent không xuất hiện trong kết quả discovery.
2. `GET /agents/{id}` trả `404 AGENT_NOT_FOUND`.
3. Agent không nhận Task mới (`AGENT_NOT_FOUND` khi được chọn làm target).
4. Toàn bộ credential của Agent bị thu hồi; các request tiếp theo dùng credential cũ nhận `401 AUTHENTICATION_FAILED`.
5. Presence key của Agent bị xoá (best-effort); sự kiện audit `agent.deregistered` được ghi.
6. Các Task đang thực thi liên quan đến Agent không bị huỷ cưỡng bức; chúng tiếp tục đến trạng thái kết thúc hoặc hết hạn (`TASK_TIMEOUT`).

### 2.3. Tổng hợp endpoint Registry

| Endpoint | Credential | Request | Thành công | Lỗi có thể xảy ra |
|---|---|---|---|---|
| `POST /agents/register` | `X-Registration-Key` | `AgentRegistrationRequest` | `201` + `AgentRegistrationResponse` | 400, 401 |
| `POST /agents/{id}/heartbeat` | `X-Api-Key` | `HeartbeatRequest` (tuỳ chọn) | `204` | 400, 401, 403 (`AGENT_ACCESS_FORBIDDEN`), 404 |
| `DELETE /agents/{id}` | `X-Api-Key` | rỗng | `204` | 400, 401, 403 (`AGENT_ACCESS_FORBIDDEN`), 404 |
| `GET /agents/{id}` | `X-Api-Key` | — | `200` + `AgentDetailResponse` | 401, 403 (`DOMAIN_FORBIDDEN`), 404 |
| `GET /agents` | `X-Api-Key` | query `domain`, `capability`, `onlineOnly` | `200` + `AgentDiscoveryResult` | 401, 403 — *triển khai ở Tuần 5* |
| `GET /agents/{id}/capabilities` | `X-Api-Key` | — | `200` + danh sách `Capability` | 401, 403, 404 — *triển khai ở Tuần 5* |
| `GET /health` | không | — | `200` khi PostgreSQL và Redis khả dụng | `503` |

Giá trị `{id}` không phải UUID hợp lệ: `400 VALIDATION_ERROR`.

---

## 3. Task Model

### 3.1. Task Status

```json
{
  "$id": "https://aers.local/schemas/task-status.schema.json",
  "type": "string",
  "enum": ["submitted", "working", "completed", "failed"]
}
```

Lifecycle: `submitted → working → completed` hoặc `submitted → working → failed`. Một số điều kiện
Policy reject có thể khiến Task chuyển thẳng `submitted → failed`.

Từ R3, các chuyển trạng thái do AERS chủ động gây ra đều là compare-and-set trên PostgreSQL (chỉ một bên thắng):

| Nguyên nhân | Chuyển | Mã lỗi | `retryable` |
|---|---|---|---|
| Quá `deadlineAt` (mục 11) | `submitted`/`working → failed` | `TASK_TIMEOUT` | `false` |
| Target không nối lại trong thời gian ân hạn (mục 11.2) | `working → failed` | `TARGET_UNAVAILABLE` | `true` |
| Không relay được tới target lúc dispatch | `submitted → failed` | `TARGET_UNAVAILABLE` | `true` |

Một `task.result` đến sau khi Task đã ở trạng thái terminal bị từ chối `TASK_STATE_CONFLICT`; kết quả đã ghi không đổi.

### 3.2. Task (persistence-facing entity)

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/task.schema.json",
  "title": "AERS Task",
  "type": "object",
  "required": [
    "taskId", "callerAgentId", "targetAgentId", "capability",
    "status", "input", "rootTaskId", "depth", "createdAt"
  ],
  "properties": {
    "taskId": { "type": "string" },
    "callerAgentId": { "type": "string" },
    "targetAgentId": { "type": "string" },
    "capability": { "type": "string" },
    "status": { "$ref": "task-status.schema.json" },
    "input": { "type": "object" },
    "output": { "type": ["object", "null"] },
    "parentTaskId": { "type": ["string", "null"] },
    "rootTaskId": { "type": "string" },
    "depth": { "type": "integer", "minimum": 0 },
    "createdAt": { "type": "string", "format": "date-time" },
    "startedAt": { "type": ["string", "null"], "format": "date-time" },
    "completedAt": { "type": ["string", "null"], "format": "date-time" },
    "error": { "anyOf": [{ "$ref": "error.schema.json" }, { "type": "null" }] }
  },
  "additionalProperties": false
}
```

**Trường chỉ thuộc lớp persistence.** Persistence Entity của Task lưu thêm hai giá trị không thuộc wire protocol và không được đưa vào `Task`, `TaskInvocation` hay `TaskEnvelope`:

- `ancestorChain` — mảng `agentId`, do AERS tính và lưu (mục 4);
- `deadlineAt` — thời hạn của Task, phục vụ background worker quét timeout (mục 11) và việc truy vấn khi TTL Redis đã hết. Giá trị này cũng được phản ánh trong `TaskRuntimeState` (mục 6.4).
- `acknowledgedAt` (cột `acknowledged_at`, nullable) — thời điểm caller xác nhận bằng `task.ack` (mục 6.6) đã xử lý kết quả terminal. Không có trong `TaskQueryResult`. Task terminal có `acknowledgedAt = null` được gửi lại khi caller nối lại.

---

## 4. Task Ancestry & Cycle Protection

`ancestorChain` là state phía server, biểu diễn **đường đi tổ tiên trực tiếp** theo `parentTaskId` của
nhánh Task đang chờ/đang xử lý liên quan — không phải toàn bộ Agent từng xuất hiện ở bất kỳ đâu trong
cây root Task.

Caller **chỉ gửi `parentTaskId`**. AERS tự tính và lưu `ancestorChain`:

```text
ancestorChain_new = ancestorChain_parent + [targetAgentId của parent]
```

**Trường hợp cơ sở (root Task).** Với Task không có `parentTaskId`, AERS khởi tạo `ancestorChain = [callerAgentId]`. Công thức trên chỉ áp dụng cho Task con. Nếu root Task được khởi tạo với chuỗi rỗng, Agent gọi gốc không bao giờ xuất hiện trong chuỗi và vòng gọi quay về Agent đó không thể bị phát hiện.

Ví dụ (A → B → C → A):

| Task | caller → target | parent | `ancestorChain` | Kết quả kiểm tra `target ∈ ancestorChain` |
|---|---|---|---|---|
| T1 | A → B | — | `[A]` | B ∉ `[A]` — chấp nhận |
| T2 | B → C | T1 | `[A]` + `[B]` = `[A, B]` | C ∉ `[A, B]` — chấp nhận |
| T3 | C → A | T2 | `[A, B]` + `[C]` = `[A, B, C]` | A ∈ `[A, B, C]` — **`CYCLE_DETECTED`** |

Trường hợp tự gọi (A → A) cũng bị chặn do `target = A ∈ [A]`.

**Độ sâu.** `depth` của root Task là 0, của Task con là `depth` của cha + 1. Task bị từ chối `DEPTH_EXCEEDED` khi `depth > MaxRecursionDepth` (mặc định 5), tức là chuỗi tối đa `MaxRecursionDepth + 1` Task được chấp nhận. Kiểm tra depth trước kiểm tra cycle.

### 4.1. CycleCheckContext (internal — không phải wire protocol)

```json
{
  "$id": "https://aers.local/schemas/cycle-check-context.schema.json",
  "type": "object",
  "required": ["rootTaskId", "ancestorChain", "targetAgentId"],
  "properties": {
    "rootTaskId": { "type": "string" },
    "ancestorChain": {
      "type": "array",
      "items": { "type": "string" },
      "description": "Đường đi tổ tiên trực tiếp theo parentTaskId, do AERS tự tính. Agent không gửi và không thể chỉnh sửa."
    },
    "targetAgentId": { "type": "string" }
  },
  "additionalProperties": false
}
```

### 4.2. Quy tắc phát hiện cycle

```text
targetAgentId ∈ ancestorChain  →  REJECT (CYCLE_DETECTED)
```

Kiểm tra ở **cấp độ Agent** — không phân biệt theo capability được gọi. Nếu Agent M đã nằm trong đường
đi tổ tiên đang chờ (waiting chain) của Task hiện tại, mọi lời gọi ngược lại M — bất kể capability nào —
đều bị chặn, vì AERS không có cách xác minh an toàn liệu tiến trình M có đang rảnh để xử lý song song
hay không trong lúc vẫn đang block chờ kết quả từ nhánh gọi hiện tại.

Hai nhánh song song trong cùng một root Task, không ai là tổ tiên trực tiếp của ai, không bị ảnh hưởng
bởi quy tắc này.

---

## 5. Task Operations (Application DTO)

### 5.1. Create Task Request

```json
{
  "$id": "https://aers.local/schemas/create-task-request.schema.json",
  "type": "object",
  "required": ["targetAgentId", "capability", "input"],
  "properties": {
    "targetAgentId": { "type": "string" },
    "capability": { "type": "string" },
    "input": { "type": "object" },
    "parentTaskId": { "type": ["string", "null"] }
  },
  "additionalProperties": false
}
```

`callerAgentId` luôn được AERS derive từ `AuthenticatedAgentContext`, không lấy từ JSON body.

### 5.2. Task Result

```json
{
  "$id": "https://aers.local/schemas/task-result.schema.json",
  "type": "object",
  "required": ["taskId", "status"],
  "properties": {
    "taskId": { "type": "string" },
    "status": { "$ref": "task-status.schema.json" },
    "output": { "type": ["object", "null"] },
    "error": { "anyOf": [{ "$ref": "error.schema.json" }, { "type": "null" }] }
  },
  "additionalProperties": false
}
```

### 5.3. Task Status Event (push notification)

```json
{
  "$id": "https://aers.local/schemas/task-status-event.schema.json",
  "type": "object",
  "required": ["taskId", "status"],
  "properties": {
    "taskId": { "type": "string" },
    "status": { "$ref": "task-status.schema.json" },
    "result": { "anyOf": [{ "$ref": "task-result.schema.json" }, { "type": "null" }] }
  },
  "additionalProperties": false
}
```

### 5.4. Task Context

```json
{
  "$id": "https://aers.local/schemas/task-context.schema.json",
  "type": "object",
  "required": ["taskId", "rootTaskId", "depth"],
  "properties": {
    "taskId": { "type": "string" },
    "parentTaskId": { "type": ["string", "null"] },
    "rootTaskId": { "type": "string" },
    "depth": { "type": "integer", "minimum": 0 },
    "correlationId": { "type": ["string", "null"] }
  },
  "additionalProperties": false
}
```

---

## 6. Wire Envelope (SignalR)

### 6.1. A2A Request Context

```json
{
  "$id": "https://aers.local/schemas/a2a-request-context.schema.json",
  "type": "object",
  "required": ["callerAgentId", "targetAgentId", "task"],
  "properties": {
    "callerAgentId": { "type": "string" },
    "targetAgentId": { "type": "string" },
    "task": { "$ref": "task.schema.json" }
  },
  "additionalProperties": false
}
```

`callerAgentId` luôn được derive từ authenticated identity ở runtime, không tin giá trị client gửi.

### 6.2. Task Envelope

Discriminated union theo `messageType`:

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/task-envelope.schema.json",
  "title": "AERS Task Envelope",
  "type": "object",
  "required": ["messageType", "payload"],
  "properties": {
    "messageType": {
      "type": "string",
      "enum": ["task.create", "task.invoke", "task.result", "task.status_event", "task.ack"]
    },
    "correlationId": { "type": ["string", "null"] },
    "payload": { "type": "object" }
  },
  "oneOf": [
    { "properties": { "messageType": { "const": "task.create" }, "payload": { "$ref": "create-task-request.schema.json" } } },
    { "properties": { "messageType": { "const": "task.invoke" }, "payload": { "$ref": "task-invocation.schema.json" } } },
    { "properties": { "messageType": { "const": "task.result" }, "payload": { "$ref": "task-result.schema.json" } } },
    { "properties": { "messageType": { "const": "task.status_event" }, "payload": { "$ref": "task-status-event.schema.json" } } },
    { "properties": { "messageType": { "const": "task.ack" }, "payload": { "$ref": "task-ack.schema.json" } } }
  ],
  "additionalProperties": false
}
```

Chiều gửi: Agent → AERS gồm `task.create`, `task.result`, `task.status_event`, `task.ack`; AERS → Agent gồm `task.invoke`, `task.result`, `task.status_event`.

`correlationId` là optional — Task/TaskInvocation đã có `taskId`/`rootTaskId`/`parentTaskId`/`depth`
đảm nhiệm correlation; `correlationId` chỉ cần khi có observability/tracing.

### 6.3. Task Invocation — AERS → Target Agent

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/task-invocation.schema.json",
  "title": "AERS Task Invocation",
  "type": "object",
  "required": ["taskId", "rootTaskId", "depth", "capability", "input", "policyContext"],
  "properties": {
    "taskId": { "type": "string" },
    "rootTaskId": { "type": "string" },
    "parentTaskId": { "type": ["string", "null"] },
    "depth": { "type": "integer", "minimum": 0 },
    "capability": { "type": "string" },
    "input": { "type": "object" },
    "policyContext": { "$ref": "policy-context.schema.json" }
  },
  "additionalProperties": false
}
```

`ancestorChain` không xuất hiện trong payload này — đây là state nội bộ AERS, không gửi cho Target Agent.

### 6.4. Task Runtime State (Redis)

```json
{
  "$id": "https://aers.local/schemas/task-runtime-state.schema.json",
  "type": "object",
  "required": ["taskId", "status", "updatedAt"],
  "properties": {
    "taskId": { "type": "string" },
    "status": { "$ref": "task-status.schema.json" },
    "updatedAt": { "type": "string", "format": "date-time" },
    "connectionId": { "type": ["string", "null"] },
    "instanceId": {
      "type": ["string", "null"],
      "description": "AERS instance đang giữ connection/xử lý Task này — hỗ trợ debug multi-instance, không dùng cho routing nghiệp vụ."
    },
    "deadlineAt": { "type": ["string", "null"], "format": "date-time" }
  },
  "additionalProperties": false
}
```

- **Redis key:** `aers:task:{taskId}` (Hash chứa các field trên).
- **TTL:** `deadlineAt - now + 60 giây` (`RuntimeStateGraceSeconds`) khi tạo Task và ở mỗi trạng thái chưa terminal; **60 giây** (`RuntimeStateTerminalTtlSeconds`) khi Task `completed`/`failed`.
- **Chỉ ghi (R3):** PostgreSQL là nguồn sự thật; không request nào đọc `TaskRuntimeState` để trả lời. Ghi sau mỗi commit thắng compare-and-set (tạo, dispatch, kết quả, timeout), HSET và PEXPIRE trong một Lua script nguyên tử. `connectionId` chỉ có giá trị ở trạng thái `working` do dispatch ghi. Lỗi Redis chỉ ghi log, không đổi kết quả đã commit.
- Deadline/timeout chỉ đặt ở `TaskRuntimeState`, **không** đưa vào `Task`/`TaskInvocation` gửi cho Agent.

### 6.5. Audit Event

```json
{
  "$id": "https://aers.local/schemas/audit-event.schema.json",
  "type": "object",
  "required": ["eventType", "occurredAt"],
  "properties": {
    "eventType": { "type": "string" },
    "occurredAt": { "type": "string", "format": "date-time" },
    "agentId": { "type": ["string", "null"] },
    "taskId": { "type": ["string", "null"] },
    "details": { "type": ["object", "null"] }
  },
  "additionalProperties": false
}
```

**Danh sách `eventType`:**

| eventType | Khi nào phát sinh | `details` |
|---|---|---|
| `agent.registered` | Đăng ký thành công | — |
| `agent.deregistered` | `DELETE /agents/{id}` thành công | — |
| `task.created` | `CreateTaskRequest` được chấp nhận, Task chuyển `submitted` | `{ targetAgentId, capability }`; thêm `orphanSuspect: true` khi root Task (không có `parentTaskId`) được tạo bởi Agent đang là target của ít nhất một Task `working` (gợi ý Agent bỏ sót `parentTaskId`; không từ chối request) |
| `task.status_changed` | Bất kỳ chuyển trạng thái Task nào | `{ fromStatus, toStatus }` |
| `policy.rejected` | Policy Check từ chối | `{ errorCode }` |

Heartbeat không được ghi thành audit event (tần suất quá cao so với mục đích audit) — trạng thái
online/offline theo dõi qua `AgentRuntimeStatus`. Lỗi xác thực cũng không ghi audit event (chống làm đầy bảng); chúng chỉ được log có giới hạn tần suất (mục 8.5).

### 6.6. Push, `task.ack` và gửi lại kết quả

**Push.** AERS push `task.result` (Task terminal) và `task.status_event` với `status = working` cho caller. Payload `task.status_event` luôn có đủ ba trường: `{ "taskId": "...", "status": "working", "result": null }`. Push là best-effort: caller không có kết nối hoặc gửi lỗi chỉ được log; không có kho lưu "chưa giao".

**`task.ack` (Agent → AERS).** Caller xác nhận đã xử lý kết quả terminal của một Task:

```json
{
  "$id": "https://aers.local/schemas/task-ack.schema.json",
  "type": "object",
  "required": ["taskId"],
  "properties": { "taskId": { "type": "string" } },
  "additionalProperties": false
}
```

Phản hồi là `EnvelopeAck` với `accepted = true`, `taskId` và `status` của Task. Idempotent: ack lặp lại vẫn được chấp nhận. Lỗi: `VALIDATION_ERROR` (taskId sai định dạng), `TASK_NOT_FOUND`, `TASK_ACCESS_FORBIDDEN` (người gửi không phải caller của Task), `TASK_STATE_CONFLICT` (Task chưa terminal), `AUTHENTICATION_FAILED` (Agent không còn active).

**Gửi lại.** Khi một Agent nối SignalR, AERS (không nằm trên đường kết nối) gửi lại `task.result` cho các Task terminal của Agent đó có `acknowledgedAt = null`, sắp theo `(completedAt, taskId)`, tối đa `RedeliveryBatchSize` (100) mỗi lần nối. Giao **ít nhất một lần**: cùng một kết quả có thể đến nhiều lần (push trực tiếp chen với gửi lại, hoặc nối lại liên tiếp) cho tới khi caller gửi `task.ack`; client xử lý `task.result` idempotent theo `taskId`. `task.invoke` và `task.status_event` không bao giờ được gửi lại.

---

## 7. Task Query API (REST)

### 7.1. `GET /tasks/{taskId}`

Cho phép Agent đọc lại trạng thái/kết quả hiện tại của một Task đã biết `taskId` — dùng khi Agent nghi
ngờ đã lỡ một push notification (ví dụ sau khi mất kết nối tạm thời và kết nối lại). Đây là kênh đọc bổ
sung cho cơ chế push qua SignalR, không thay thế push.

**Authorization:** chỉ `callerAgentId` hoặc `targetAgentId` của chính Task đó (xác định từ
`AuthenticatedAgentContext.agentId`, không tin path param) mới được phép đọc.

### 7.2. Task Query Result

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://aers.local/schemas/task-query-result.schema.json",
  "title": "AERS Task Query Result",
  "type": "object",
  "required": ["taskId", "status", "rootTaskId", "depth", "createdAt"],
  "properties": {
    "taskId": { "type": "string" },
    "callerAgentId": { "type": "string" },
    "targetAgentId": { "type": "string" },
    "capability": { "type": "string" },
    "status": { "$ref": "task-status.schema.json" },
    "output": { "type": ["object", "null"] },
    "parentTaskId": { "type": ["string", "null"] },
    "rootTaskId": { "type": "string" },
    "depth": { "type": "integer", "minimum": 0 },
    "createdAt": { "type": "string", "format": "date-time" },
    "startedAt": { "type": ["string", "null"], "format": "date-time" },
    "completedAt": { "type": ["string", "null"], "format": "date-time" },
    "error": { "anyOf": [{ "$ref": "error.schema.json" }, { "type": "null" }] }
  },
  "additionalProperties": false
}
```

`input` không nằm trong response này để tránh lộ lại payload gốc qua kênh query riêng.

### 7.3. Luồng

```text
Agent (caller hoặc target của Task) --> GET /tasks/{taskId}
AERS: Authenticate
AERS: đọc TaskRuntimeState (Redis), fallback PostgreSQL nếu TTL đã hết
AERS: kiểm tra caller ∈ {callerAgentId, targetAgentId} của Task
    → không thoả: 403 TASK_ACCESS_FORBIDDEN
    → không tìm thấy Task: 404 TASK_NOT_FOUND
    → hợp lệ: 200 TaskQueryResult
```

---

## 8. Authentication

### 8.1. Cơ chế và loại credential

Cơ chế xác thực được triển khai trong phạm vi MVP là **API Key**. JWT chưa được triển khai; giá trị `JWT` trong `authenticationScheme` được giữ làm điểm mở rộng.

| Loại credential | Bên cấp | Header | Phạm vi hiệu lực |
|---|---|---|---|
| Registration key | AERS Operator (cấu hình triển khai) | `X-Registration-Key` | Chỉ `POST /agents/register` |
| Agent API key | AERS, tại thời điểm đăng ký thành công | `X-Api-Key` | Mọi endpoint REST còn lại và kết nối SignalR của Agent |

Nguyên tắc bất biến: `agentId` của caller **luôn** được suy ra từ credential đã xác thực; mọi giá trị `agentId` trong route, header khác hoặc body chỉ được dùng làm đối tượng của thao tác và được đối chiếu với danh tính đã xác thực.

### 8.2. Agent API key

- Định dạng: tiền tố `aers_` theo sau là chuỗi base64url (không đệm) của 32 byte sinh từ bộ sinh số ngẫu nhiên mật mã.
- Lưu trữ: chỉ lưu giá trị băm SHA-256 (hex, chữ thường); không lưu key thô. Giá trị băm có ràng buộc duy nhất.
- Phân giải: băm giá trị trong `X-Api-Key`, tra cứu credential chưa bị thu hồi thuộc Agent chưa deregister; kết quả cho ra `agentId` và `domain`.
- Thu hồi: toàn bộ credential của Agent bị thu hồi khi deregister. Giai đoạn MVP không có cơ chế xoay vòng (rotate) hay khôi phục key.

### 8.3. Registration key

- Nguồn: section `Aers:Auth:RegistrationKeys` (danh sách chuỗi) từ cấu hình, biến môi trường hoặc user-secrets; không được lưu trong mã nguồn.
- So sánh theo thời gian hằng số (constant-time).
- Ứng dụng từ chối khởi động nếu danh sách rỗng ngoài môi trường Development.

### 8.4. Kết nối SignalR

Agent sử dụng cùng Agent API key để xác thực kết nối SignalR. Cách truyền credential tại bước thiết lập kết nối (header hoặc cơ chế tương đương) được chốt khi triển khai Hub ở Tuần 6.

### 8.5. Xử lý lỗi xác thực

Credential thiếu, sai, đã bị thu hồi hoặc thuộc Agent đã deregister đều trả `401 AUTHENTICATION_FAILED` với thông điệp chung, không phân biệt nguyên nhân. Giá trị credential (thô hoặc băm) không được ghi vào log.

Từ R3, mỗi lỗi xác thực được log một cảnh báo có cấu trúc gồm scheme, lý do (`missing`, `malformed`, `unknown_or_revoked`), path, địa chỉ nguồn và cờ hub; tối đa một log mỗi 10 giây cho mỗi cặp (địa chỉ, lý do), bộ nhớ giới hạn 10.000 mục. Không ghi vào cơ sở dữ liệu.

**Thu hồi kết nối.** Khi Agent bị deregister, AERS publish `agentId` lên kênh Redis `{prefix}agent:revoked`; mọi instance subscribe và instance đang giữ kết nối SignalR của Agent đó sẽ abort kết nối. Do pub/sub không lưu tin, mọi message `task.create`, `task.result`, `task.status_event`, `task.ack` còn kiểm tra lại Agent gửi vẫn active; nếu không: `AUTHENTICATION_FAILED`.

### 8.6. AuthenticatedAgentContext

```json
{
  "$id": "https://aers.local/schemas/authenticated-agent-context.schema.json",
  "type": "object",
  "required": ["agentId"],
  "properties": {
    "agentId": { "type": ["string", "null"], "description": "Agent identity sau khi AERS xác thực credential; null khi xác thực bằng registration key." },
    "domain": { "type": ["string", "null"] },
    "authenticationScheme": { "type": ["string", "null"], "enum": ["JWT", "API_KEY", null] }
  },
  "additionalProperties": false
}
```

Đây là internal context sau khi xác thực; giá trị credential không nằm trong schema này.

---

## 9. Access Policy

### 9.1. Policy Context

```json
{
  "$id": "https://aers.local/schemas/policy-context.schema.json",
  "type": "object",
  "required": ["callerAgentId", "targetAgentId", "capability"],
  "properties": {
    "callerAgentId": { "type": "string" },
    "targetAgentId": { "type": "string" },
    "capability": { "type": "string" },
    "callerDomain": { "type": ["string", "null"] },
    "targetDomain": { "type": ["string", "null"] },
    "rootTaskId": { "type": ["string", "null"] },
    "parentTaskId": { "type": ["string", "null"] },
    "depth": { "type": ["integer", "null"], "minimum": 0 },
    "ownerRef": { "type": ["string", "null"] }
  },
  "additionalProperties": true
}
```

`additionalProperties: true` cho phép thêm điều kiện policy mới trong tương lai mà không phá protocol
hiện tại.

### 9.2. Policy Result

```json
{
  "$id": "https://aers.local/schemas/policy-result.schema.json",
  "type": "object",
  "required": ["allowed"],
  "properties": {
    "allowed": { "type": "boolean" },
    "reason": { "type": ["string", "null"] },
    "error": { "anyOf": [{ "$ref": "error.schema.json" }, { "type": "null" }] }
  },
  "additionalProperties": false
}
```

### 9.3. Cấu hình Policy (`AersPolicyOptions`)

Toàn bộ tham số dưới đây được cấu hình qua `appsettings`, không hard-code trong logic.

| Tham số | Giá trị |
|---|---|
| `MaxRecursionDepth` | **5** |
| `DefaultTaskTimeoutSeconds` | **300** |
| `RateLimit.WindowSeconds` | **60** |
| `RateLimit.MaxTasksPerWindow` | **100** |
| `Heartbeat.IntervalSeconds` | **15** |
| `Heartbeat.OfflineThresholdSeconds` | **45** |
| `CycleDetection.Granularity` | **AgentOnly** |
| `DomainPolicy.Mode` | **DenyByDefault** (cross-domain chỉ được phép khi khai báo trong allowlist) |
| `DomainPolicy.CrossDomainRules` | Danh sách cặp `(callerDomain, targetDomain)` được phép, cấu hình qua appsettings |
| `Auth.RegistrationKeys` | Danh sách registration key; không có giá trị mặc định; bắt buộc không rỗng ngoài môi trường Development |
| `RegistrationLimits.MaxNameLength` | **200** |
| `RegistrationLimits.MaxCapabilities` | **50** |
| `RegistrationLimits.MaxSchemaBytes` | **65536** |
| `RegistrationLimits.MaxBodyBytes` | **1048576** |
| `Redis.KeyPrefix` | **`aers:`** |
| `Tasks.TimeoutScanIntervalSeconds` | **5** |
| `Tasks.TimeoutBatchSize` | **100** |
| `Tasks.RuntimeStateGraceSeconds` | **60** |
| `Tasks.RuntimeStateTerminalTtlSeconds` | **60** |
| `Tasks.RedeliveryBatchSize` | **100** |
| `Tasks.TargetDisconnectGraceSeconds` | **30** |

### 9.4. Thứ tự kiểm tra Policy

Từ R3 (ADR-W8-01), rate limit chạy sớm, ngay sau khi biết caller hợp lệ:

```text
Authenticated?
      ↓
Payload đúng hình dạng? (lỗi: VALIDATION_ERROR — không tính quota)
      ↓
Caller tồn tại và active?            (không tính quota)
      ↓
Rate limit OK?                       (từ đây mỗi request tiêu một đơn vị quota)
      ↓
Target tồn tại?
      ↓
Quy tắc parentTaskId?
      ↓
Domain scope allowed?
      ↓
Capability exists ở target?
      ↓
Input hợp lệ theo inputSchema của capability?   (lỗi: VALIDATION_ERROR)
      ↓
Recursion depth OK?
      ↓
Cycle OK?
      ↓
Tạo Task + Relay
```

Mọi từ chối từ bước rate limit trở đi ghi audit `policy.rejected { errorCode }` và không tạo Task. Request bị từ chối ở bước sau rate limit vẫn đã tiêu một đơn vị quota.

---

## 10. Rate Limiting

```json
{
  "$id": "https://aers.local/schemas/rate-limit-decision.schema.json",
  "type": "object",
  "required": ["allowed"],
  "properties": {
    "allowed": { "type": "boolean" },
    "limit": { "type": ["integer", "null"], "minimum": 0 },
    "remaining": { "type": ["integer", "null"], "minimum": 0 },
    "resetAt": { "type": ["string", "null"], "format": "date-time" }
  },
  "additionalProperties": false
}
```

**Thuật toán:** fixed window counter theo `callerAgentId`, dùng Redis `INCR` + `EXPIRE` trong một Lua script (key không bao giờ tồn tại mà thiếu TTL). Key: `{prefix}ratelimit:{callerAgentId}:{windowStartUnixSeconds}`, với `windowStart = floor(now / WindowSeconds) * WindowSeconds`. Request bị từ chối vẫn tăng bộ đếm; `remaining` không bao giờ âm; `resetAt` là cuối cửa sổ hiện tại (UTC). Đơn vị: số
lệnh `task.create` mỗi cửa sổ (không tính `task.result`/`task.status_event`). Không rate-limit theo IP —
mọi caller đã có identity qua JWT/API Key.

**Khi Redis không khả dụng: fail-closed.** `task.create` bị từ chối với `INTERNAL_ERROR`, `retryable: true`, có audit `policy.rejected`; không tạo Task.

---

## 11. Task Timeout

### 11.1. Deadline và quét timeout

`CreateTaskRequest` không mang field timeout/deadline. `deadlineAt` do AERS tự gán tại thời điểm tạo
Task:

```text
root Task:   deadlineAt = createdAt + DefaultTaskTimeoutSeconds (300 giây)
Task con:    deadlineAt = min(createdAt + DefaultTaskTimeoutSeconds, deadlineAt của Task cha)
```

Task con không bao giờ sống lâu hơn Task cha. Nếu Task cha đã quá hạn lúc tạo con, con vẫn được tạo với deadline của cha và bị worker xử lý ở lượt quét kế tiếp.

Một background worker chạy trên **mọi instance** (chu kỳ `TimeoutScanIntervalSeconds`, mỗi truy vấn tối đa `TimeoutBatchSize`) quét các Task `submitted`/`working` đã quá `deadlineAt`, chuyển Task sang `failed` với `TASK_TIMEOUT` bằng compare-and-set trên PostgreSQL nên mỗi Task chỉ chuyển một lần dù nhiều instance cùng chạy. Sau commit, worker push `task.result` cho caller (best-effort). TTL Redis của `TaskRuntimeState` (mục 6.4) chỉ dọn dữ liệu, không phải cơ chế phát hiện timeout.

### 11.2. Target mất kết nối

Khi kết nối SignalR của một Agent đóng và đó vẫn là kết nối hiện tại trong registry, AERS ghi thời điểm ngắt vào Redis sorted set `{prefix}disconnected` (member `agentId`, score unix mili-giây); kết nối mới của Agent xoá dấu này. AERS **không** fail Task ngay vì target có thể nối lại và vẫn trả kết quả. Sau `TargetDisconnectGraceSeconds` (30 giây) mà Agent vẫn không có kết nối, worker chuyển mọi Task `working` có target là Agent đó sang `failed` với `TARGET_UNAVAILABLE` (`retryable: true`) bằng compare-and-set, rồi push cho caller. Một `task.result` đến trước thắng; đến sau nhận `TASK_STATE_CONFLICT`. Mất dấu Redis chỉ khiến Task kết thúc bằng `TASK_TIMEOUT`. Caller mất kết nối không ảnh hưởng Task của nó: kết quả được gửi lại khi nối lại (mục 6.6).

---

## 12. Error Model

### 12.1. Error

```json
{
  "$id": "https://aers.local/schemas/error.schema.json",
  "type": "object",
  "required": ["code", "message"],
  "properties": {
    "code": { "type": "string" },
    "message": { "type": "string" },
    "details": { "type": ["object", "null"] },
    "retryable": { "type": ["boolean", "null"] }
  },
  "additionalProperties": false
}
```

`code` giữ kiểu `string` tự do (không `enum`) để có thể bổ sung mã lỗi mới mà không phải breaking
change.

### 12.2. Bộ mã lỗi baseline

| Mã | Tình huống |
|---|---|
| `AUTHENTICATION_FAILED` | Credential (API Key/registration key) thiếu, không hợp lệ hoặc đã bị thu hồi |
| `AGENT_NOT_FOUND` | `callerAgentId`/`targetAgentId` không tồn tại trong registry |
| `CAPABILITY_NOT_FOUND` | Capability được gọi không tồn tại ở target |
| `DOMAIN_FORBIDDEN` | Cross-domain call không được policy cho phép |
| `RATE_LIMITED` | Vượt rate limit |
| `DEPTH_EXCEEDED` | Vượt `MaxRecursionDepth` |
| `CYCLE_DETECTED` | `targetAgentId` đã có trong `ancestorChain` |
| `TARGET_UNAVAILABLE` | Target Agent offline ngay lúc relay |
| `TASK_TIMEOUT` | Task quá `deadlineAt` trước khi hoàn thành |
| `VALIDATION_ERROR` | Payload không khớp schema |
| `TASK_NOT_FOUND` | `taskId` không tồn tại (khi gọi `GET /tasks/{taskId}`) |
| `TASK_ACCESS_FORBIDDEN` | Task tồn tại nhưng caller không phải bên tham gia Task đó |
| `AGENT_ACCESS_FORBIDDEN` | Agent thao tác (heartbeat, deregister) lên Agent khác với chính mình |
| `INTERNAL_ERROR` | Lỗi hệ thống không xác định |

### 12.3. Ánh xạ mã lỗi sang HTTP status

Mọi response lỗi có body là `Error` (mục 12.1) với `Content-Type: application/json`. Lỗi không lường trước trả `500 INTERNAL_ERROR` với thông điệp chung; chi tiết nội bộ và stack trace không được đưa vào response.

| Mã | HTTP status |
|---|---|
| `VALIDATION_ERROR` | 400 |
| `AUTHENTICATION_FAILED` | 401 |
| `AGENT_ACCESS_FORBIDDEN`, `DOMAIN_FORBIDDEN`, `TASK_ACCESS_FORBIDDEN` | 403 |
| `AGENT_NOT_FOUND`, `TASK_NOT_FOUND`, `CAPABILITY_NOT_FOUND` | 404 |
| `RATE_LIMITED` | 429 |
| `INTERNAL_ERROR` | 500 |
| `TASK_STATE_CONFLICT`, `DEPTH_EXCEEDED`, `CYCLE_DETECTED` | 409 |
| `TARGET_UNAVAILABLE` | 503 |
| `TASK_TIMEOUT` | Chỉ xuất hiện trong `task.result` (kênh SignalR) và `TaskQueryResult.error`; không phải mã trả cho một request REST |

**Cấu trúc `details` (R3).** Các mã sau mang `details` cố định và `retryable`; ngoài ra `details` chỉ là `{ "errors": [...] }` của `VALIDATION_ERROR`:

| Mã | `details` | `retryable` |
|---|---|---|
| `RATE_LIMITED` | `{ "limit": 100, "remaining": 0, "resetAt": "<ISO-8601 UTC>" }` | `true` |
| `DEPTH_EXCEEDED` | `{ "maxDepth": 5, "depth": 6 }` | `false` |
| `CYCLE_DETECTED` | `{ "rootTaskId": "<uuid>" }` — không trả `ancestorChain` | `false` |
| `INTERNAL_ERROR` do dịch vụ runtime không khả dụng | không có | `true` |

---

## Phụ lục — Chỉ mục Schema

| Schema | `$id` |
|---|---|
| Capability | `capability.schema.json` |
| AgentCardSubmission | `agent-card-submission.schema.json` |
| AgentCard | `agent-card.schema.json` |
| AgentRegistrationRequest / Response | `agent-registration-request.schema.json` / `agent-registration-response.schema.json` |
| AgentDiscoveryResult | `agent-discovery-result.schema.json` |
| AgentRuntimeStatus | `agent-runtime-status.schema.json` |
| AgentDetailResponse | `agent-detail-response.schema.json` |
| HeartbeatRequest | `heartbeat-request.schema.json` |
| DeregistrationRequest | `deregistration-request.schema.json` |
| TaskStatus | `task-status.schema.json` |
| Task | `task.schema.json` |
| CycleCheckContext | `cycle-check-context.schema.json` |
| CreateTaskRequest | `create-task-request.schema.json` |
| TaskResult | `task-result.schema.json` |
| TaskStatusEvent | `task-status-event.schema.json` |
| TaskContext | `task-context.schema.json` |
| A2ARequestContext | `a2a-request-context.schema.json` |
| TaskEnvelope | `task-envelope.schema.json` |
| TaskInvocation | `task-invocation.schema.json` |
| TaskRuntimeState | `task-runtime-state.schema.json` |
| AuditEvent | `audit-event.schema.json` |
| TaskQueryResult | `task-query-result.schema.json` |
| TaskAck | `task-ack.schema.json` |
| AuthenticatedAgentContext | `authenticated-agent-context.schema.json` |
| PolicyContext | `policy-context.schema.json` |
| PolicyResult | `policy-result.schema.json` |
| RateLimitDecision | `rate-limit-decision.schema.json` |
| Error | `error.schema.json` |

---

## Lịch sử sửa đổi

| Revision | Nội dung |
|---|---|
| R1 | Bản Final của Tuần 2. |
| R2 | Đồng bộ trước Tuần 4: (1) `agent-registration-response` bổ sung `apiKey`, quy định hai loại credential và header tương ứng (mục 1.4, 8); (2) bổ sung quy tắc kiểm tra AgentCardSubmission (mục 1.2.1); (3) bổ sung `agent-detail-response` và quy tắc truy cập `GET /agents/{id}` (mục 1.7–1.8); (4) làm rõ heartbeat, presence key `lastbeat`, deregistration dạng xoá mềm và bảng endpoint Registry (mục 2); (5) bổ sung trường hợp cơ sở của `ancestorChain` cho root Task (mục 4); (6) ghi nhận các trường chỉ thuộc persistence (mục 3.2); (7) bổ sung bước kiểm tra `input` theo `inputSchema` vào thứ tự Policy (mục 9.4); (8) bổ sung mã lỗi `AGENT_ACCESS_FORBIDDEN` và ánh xạ HTTP (mục 12.2–12.3); (9) bổ sung tham số cấu hình `Auth`, `RegistrationLimits`, `Redis.KeyPrefix` (mục 9.3). Chi tiết lý do: `05-decision-log-v3.md`. |
| R3 | Đồng bộ Tuần 7: (1) thêm `task.ack`, schema `TaskAck`, push `working`, gửi lại kết quả ít nhất một lần (mục 6.2, 6.6); (2) `acknowledgedAt` chỉ thuộc persistence (mục 3.2); (3) chuyển trạng thái do timeout / mất kết nối, `TASK_STATE_CONFLICT` cho kết quả đến muộn (mục 3.1, 11); (4) deadline Task con không quá deadline cha, ân hạn mất kết nối (mục 11); (5) thứ tự Policy mới với rate limit sớm, fail-closed, key Redis và `details` của `RATE_LIMITED`/`DEPTH_EXCEEDED`/`CYCLE_DETECTED` (mục 9.4, 10, 12.3); (6) thu hồi kết nối qua Redis pub/sub, log lỗi xác thực có giới hạn tần suất (mục 8.5); (7) runtime state chỉ ghi, TTL, cờ `orphanSuspect` (mục 6.4, 6.5); (8) tham số cấu hình mới (mục 9.3). Kiểm tra `output` theo `outputSchema` chỉ là cảnh báo log, Task vẫn `completed`. Lý do: `05-decision-log-v3.md` (ADR-W7-01…W9-02). |

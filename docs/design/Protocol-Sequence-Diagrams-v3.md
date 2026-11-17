# AERS — Protocol Sequence Diagrams v1.0

**Đề tài:** Phát triển hệ thống backend cho Agent-to-Agent Runtime (AERS)
**Trạng thái:** Final — Deliverable chính thức Tuần 2 (Thiết kế Protocol)
**Đi kèm:** `Protocol-Specification-v3.md` (protocol v1.0, Revision R2)
**Revision:** R3 — đồng bộ Tuần 7 (xem cuối tài liệu)

---

## 1. Task Lifecycle (State Diagram)

```plantuml
@startuml AERS_Task_State
title AERS - Task Lifecycle

[*] --> submitted : CreateTask() sau khi Policy Check pass

submitted --> working : Target Agent nhận Task
working --> completed : Target Agent trả kết quả thành công
working --> failed : Target Agent báo lỗi / exception
submitted --> failed : Policy reject (cycle/depth/rate limit/auth)
working --> failed : timeout/deadline vượt quá

completed --> [*]
failed --> [*]

note right of working
  Task có thể tạo Task con
  (parentTaskId = Task này)
  trong lúc đang ở trạng thái working
end note

@enduml
```

---

## 2. Đăng ký Agent, Discovery, và gọi đồng bộ (A → B)

```plantuml
@startuml AERS_Sync_Sequence
title AERS - Registration, Discovery, Synchronous Task Call

participant "Agent A" as A
participant "Agent B" as B
participant "Agent Registry" as Registry
participant "Capability Discovery" as Discovery
participant "Auth + Policy" as Policy
participant "Task Manager\n+ Relay" as Task

== Đăng ký Agent (runtime, không cần deploy lại AERS) ==
B -> Registry: POST /agents/register [X-Registration-Key]\n(AgentCardSubmission: domain=jobhunt, capability=analyze_cv)
Registry -> Registry: sinh agentId (UUID v4) + Agent API key\nlưu Agent + Capability + hash(apiKey) trong một transaction
Registry --> B: 201 Created { agentId=agentId-B, apiKey }

== Khám phá ==
A -> Discovery: GET /agents?domain=jobhunt [X-Api-Key]
Discovery --> A: AgentDiscoveryResult [agentId-B: analyze_cv, ...]
A -> Discovery: GET /agents/agentId-B/capabilities [X-Api-Key]
Discovery --> A: inputSchema/outputSchema của analyze_cv

== Gọi đồng bộ ==
A -> Task: TaskEnvelope{task.create, CreateTaskRequest(target=agentId-B, capability=analyze_cv, input)}
Task -> Policy: Authenticate A + Authorize (domain scope, capability, rate limit, depth)
Policy --> Task: allowed
Task -> B: TaskEnvelope{task.invoke, TaskInvocation} (submitted -> working)
B --> Task: TaskEnvelope{task.result, TaskResult(completed, output)}
Task --> A: TaskEnvelope{task.result, TaskResult}

@enduml
```

---

## 3. Gọi bất đồng bộ + đệ quy, và cycle rejection

```plantuml
@startuml AERS_Async_Recursive_Sequence
title AERS - Asynchronous + Recursive Task Chain + Cycle Rejection

participant "Agent A" as A
participant "Agent B" as B
participant "Agent C" as C
participant "Task Manager + Relay" as Task
participant "Access Policy\n(depth/cycle check)" as Policy

== Chuỗi hợp lệ: A -> B -> C (bất đồng bộ) ==
A -> Task: task.create T1 (parentTaskId=null, target=B)
Task -> Task: rootTaskId=T1, depth=0, ancestorChain=[A]
Task -> B: task.invoke T1 (submitted)
B -> Task: task.status_event T1 = working
Task -> A: push T1 status = working

B -> Task: task.create T2 (parentTaskId=T1, target=C)
Task -> Task: rootTaskId=T1, depth=1, ancestorChain=[A]+[B]=[A, B]
Task -> Policy: CheckRecursionDepth(rootTaskId=T1, depth=1)
Policy --> Task: OK (<= MaxRecursionDepth=5)
Task -> C: task.invoke T2
C -> Task: task.result T2 completed
Task -> B: push T2 completed
B -> Task: task.result T1 completed (dùng kết quả T2)
Task -> A: push T1 completed

== Chuỗi bị chặn: C cố gọi ngược lại A ==
C -> Task: task.create T3 (parentTaskId=T2, target=A)
Task -> Task: rootTaskId=T1, depth=2, ancestorChain=[A, B]+[C]=[A, B, C]
Task -> Policy: CheckCycle(rootTaskId=T1, ancestorChain=[A, B, C], target=A)
Policy --> Task: REJECT - CYCLE_DETECTED (A ∈ ancestorChain)
Task --> C: Error(CYCLE_DETECTED, "target A không hợp lệ trong chain rootTaskId=T1")

@enduml
```

---

## 4. Heartbeat & Agent Offline Detection

```plantuml
@startuml AERS_Heartbeat_Sequence
title AERS - Heartbeat & Passive Offline Detection

participant "Agent" as A
participant "AERS" as S
database "Redis" as R
participant "Agent khác\n(Caller)" as C

loop mỗi 15 giây
  A -> S: POST /agents/{id}/heartbeat [X-Api-Key]
  S -> S: agentId (từ credential) == {id}?
  S -> R: SET aers:agent:{id}:alive "1" EX 45
  S -> R: SET aers:agent:{id}:lastbeat <thời điểm server> EX 86400
  S --> A: 204 No Content
end

note over A, S
  Agent dừng heartbeat (crash / mất kết nối)
end note

...45 giây trôi qua, không có heartbeat mới...

C -> S: GET /agents/{id} [X-Api-Key]
S -> R: EXISTS aers:agent:{id}:alive
R --> S: false (key đã hết hạn)
S -> R: GET aers:agent:{id}:lastbeat
S --> C: AgentDetailResponse { agentCard, runtimeStatus: { status: offline, lastHeartbeatAt } }

@enduml
```

---

## 5. Task Query sau khi Agent reconnect

```plantuml
@startuml AERS_Task_Query_Reconnect_Sequence
title AERS - Task Query API dùng sau khi Agent reconnect

participant "Agent A\n(Caller)" as A
participant "AERS" as S
participant "Agent B\n(Target)" as B

A -> S: task.create T1 -> B (async)
S -> B: task.invoke T1

note over A, S
  Agent A mất kết nối tạm thời
end note

B -> S: task.result T1 completed
S -> A: push task.status_event T1 (không đến nơi — A đang mất kết nối)

note over A
  Agent A reconnect
end note

A -> S: GET /tasks/T1
S -> S: đọc TaskRuntimeState (Redis), fallback PostgreSQL nếu cần
S -> S: kiểm tra A ∈ {callerAgentId, targetAgentId} của T1
S --> A: 200 TaskQueryResult { status: completed, output }

@enduml
```

---

## 6. Deregistration (xoá mềm)

```plantuml
@startuml AERS_Deregistration_Sequence
title AERS - Agent Deregistration (soft delete)

participant "Agent" as A
participant "AERS" as S
database "PostgreSQL" as DB
database "Redis" as R

A -> S: DELETE /agents/{id} [X-Api-Key]
S -> S: agentId (từ credential) == {id}?
alt khác nhau
  S --> A: 403 AGENT_ACCESS_FORBIDDEN
end
S -> DB: BEGIN\nUPDATE agents SET deregistered_at = now\nthu hồi toàn bộ credential của Agent\nINSERT audit_events (agent.deregistered)\nCOMMIT
S -> R: DEL aers:agent:{id}:alive, aers:agent:{id}:lastbeat (best-effort)
S --> A: 204 No Content

note over A, S
  Từ thời điểm này: credential cũ -> 401 AUTHENTICATION_FAILED;
  GET /agents/{id} -> 404 AGENT_NOT_FOUND;
  Agent không còn trong discovery và không nhận Task mới.
end note

@enduml
```

---

## 7. Task timeout (worker)

```plantuml
@startuml AERS_Task_Timeout_Sequence
title AERS - Task timeout (mọi instance chạy worker)

participant "TaskTimeoutWorker\n(mỗi instance)" as W
participant "TaskService" as T
database "PostgreSQL" as DB
database "Redis" as R
participant "Agent A\n(Caller)" as A

loop mỗi TimeoutScanIntervalSeconds
  W -> DB: SELECT id, status FROM tasks\nWHERE status IN (submitted, working) AND deadline_at < now\nORDER BY deadline_at, id LIMIT 100
  loop từng Task quá hạn
    W -> T: ExpireAsync(taskId)
    T -> DB: UPDATE tasks SET status = failed ... WHERE id AND status = <đã đọc>\n(compare-and-set; chỉ một instance thắng)
    alt thắng
      T -> DB: INSERT task_status_history, audit task.status_changed
      T -> R: HSET aers:task:{id} status=failed + PEXPIRE 60s (best-effort)
      W -> A: push task.result failed TASK_TIMEOUT (best-effort)
    else thua
      T --> W: Expired = false (không push)
    end
  end
end
@enduml
```

---

## 8. Push, task.ack và gửi lại kết quả

```plantuml
@startuml AERS_Ack_Redelivery_Sequence
title AERS - Redelivery of unacknowledged results (at least once)

participant "Agent A\n(Caller)" as A
participant "AgentHub" as H
participant "TaskRecoveryService" as Rec
database "PostgreSQL" as DB

note over A, H
  Kết quả của Task đã terminal nhưng push không đến được A
  (A đang mất kết nối). acknowledged_at vẫn NULL.
end note

A -> H: kết nối SignalR [X-Api-Key]
H -> H: bind connection, xoá dấu mất kết nối
H --> A: kết nối thành công
H -> Rec: RedeliverAsync(A)  (tác vụ nền, ngoài đường nối)
Rec -> DB: SELECT tasks WHERE caller = A AND status IN (completed, failed)\nAND acknowledged_at IS NULL ORDER BY completed_at, id LIMIT 100
loop từng Task
  Rec -> A: task.result
end

A -> A: xử lý task.result (idempotent theo taskId)
A -> H: task.ack { taskId }
H -> DB: UPDATE tasks SET acknowledged_at = now\nWHERE id AND caller_agent_id = A AND acknowledged_at IS NULL
H --> A: EnvelopeAck { accepted: true, taskId, status }

note over A, H
  Ack lặp lại vẫn accepted. Không ack thì lần nối sau gửi lại.
  Chỉ task.result terminal được gửi lại; task.invoke và
  task.status_event thì không.
end note
@enduml
```

---

## 9. Target mất kết nối và thời gian ân hạn

```plantuml
@startuml AERS_Disconnect_Grace_Sequence
title AERS - Target disconnect grace period

participant "Agent B\n(Target)" as B
participant "AgentHub" as H
database "Redis" as R
participant "TaskTimeoutWorker" as W
participant "TaskService" as T
participant "Agent A\n(Caller)" as A

B -> H: (kết nối đóng)
H -> R: unbind compare-and-delete aers:conn:{B}
alt đây là kết nối hiện tại
  H -> R: ZADD aers:disconnected <now ms> B
end

... TargetDisconnectGraceSeconds = 30 ...

W -> R: ZRANGEBYSCORE aers:disconnected -inf <now - 30s>
W -> R: aers:conn:{B} còn tồn tại?
alt B đã nối lại
  W -> R: ZREM aers:disconnected B
else B vẫn vắng
  W -> T: FailTasksOfDisconnectedTargetAsync(B)
  T -> T: mỗi Task working của B: compare-and-set working -> failed\n(TARGET_UNAVAILABLE, retryable)
  W -> A: push task.result failed (best-effort)
  W -> R: ZREM aers:disconnected B (khi không còn Task working)
end
@enduml
```

---

## Lịch sử sửa đổi

| Revision | Nội dung |
|---|---|
| R1 | Bản Final của Tuần 2. |
| R2 | (1) Mục 2: đăng ký dùng `X-Registration-Key`, response gồm `agentId` và `apiKey`; thay mã định danh minh hoạ `cv-agent-001` bằng `agentId-B` (UUID v4 do AERS cấp); bổ sung `X-Api-Key` cho các request sau. (2) Mục 3: `ancestorChain` của root Task là `[A]`; T2 `[A, B]`; T3 `[A, B, C]`. (3) Mục 4: heartbeat bổ sung kiểm tra danh tính, key `lastbeat`, response 204. (4) Mục 6: bổ sung sequence Deregistration. |
| R3 | Đồng bộ Tuần 7: thêm mục 7 (timeout worker), 8 (task.ack và gửi lại), 9 (ân hạn khi target mất kết nối). |

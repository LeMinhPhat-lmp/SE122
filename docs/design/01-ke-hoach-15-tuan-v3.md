# Kế hoạch triển khai 15 tuần — AERS v2

**Đề tài:** Phát triển hệ thống backend cho Agent-to-Agent Runtime  
**Hệ thống:** AERS  
**Thành viên:** 2 sinh viên  
**Thời gian:** 02/09/2026 – 29/12/2026  
**Reference domain:** JobHunt

## Nguyên tắc

Trọng tâm là Core Runtime của AERS:

```text
Agent Registry
→ Capability Discovery
→ Authentication/Policy
→ Task
→ Message Relay
→ Distributed State
→ Persistence
→ Scale/Load Test
```

AERS không xây AI Agent và không chứa LLM. Dummy Agent chỉ dùng để kiểm thử.

---

# Giai đoạn 1 — Nghiên cứu & thiết kế (Tuần 1–3)

## Tuần 1 — Nghiên cứu mô hình Agent-to-Agent

- Nghiên cứu A2A, Agent-as-a-Tool và MCP ở mức conceptual.
- Xác định Agent Card / Capability.
- Xác định Agent Registry.
- Xác định Task và Task lifecycle.
- Phân biệt rõ:
  - Agent reasoning;
  - AERS infrastructure;
  - message relay.
- Xác định các vấn đề recursive calling.

**Deliverable:** Scope v2 + conceptual model + glossary.

## Tuần 2 — Thiết kế Protocol

Thiết kế:

- Agent registration;
- Agent Card;
- Capability;
- Task envelope;
- Task status;
- error model;
- parentTaskId;
- rootTaskId;
- depth;
- correlation ID;
- synchronous request-response;
- asynchronous push.

Thiết kế lifecycle:

```text
submitted → working → completed
                       ↘ failed
```

**Deliverable:** Protocol Specification v0.1 + sequence diagrams.

## Tuần 3 — Kiến trúc

Thiết kế:

- Agent Registry;
- Discovery Service;
- Authentication;
- Policy Layer;
- Task Manager;
- Relay;
- Redis state;
- PostgreSQL persistence;
- SignalR;
- Docker.

**Deliverable:** Architecture Document + solution skeleton.

**Milestone:** Có protocol và architecture đủ để bắt đầu code.

---

# Giai đoạn 2 — Core Runtime (Tuần 4–8)

## Tuần 4 — Agent Registry + Registration

- `POST /agents/register`.
- Agent identity.
- Agent metadata (gắn `domain`; **ghi chú thiết kế**: schema chừa 1 trường mở rộng dạng nullable — ví dụ `ownerRef` — để sau này có thể gắn Tenant thật mà không phải đổi schema phá vỡ tương thích. KHÔNG xây Tenant registration/authentication ở giai đoạn này — Tenant là định hướng tương lai, xem `03-mo-ta-de-tai-v3.md` mục 7).
- Capability metadata.
- Credential: registration key do Operator cấp (chỉ để đăng ký) và Agent API key do AERS cấp khi đăng ký (chỉ lưu dạng băm).
- Heartbeat.
- Deregistration (xoá mềm).
- Online/offline status (Redis TTL), quan sát qua `GET /agents/{id}`.
- Validation Agent Card theo JSON Schema Draft 2020-12.

**Deliverable:** Agent có thể register và xuất hiện trong registry ngay khi AERS đang chạy (không cần down/redeploy).

## Tuần 5 — Capability Discovery

- `GET /agents`
- `GET /agents/{id}` (đã triển khai từ Tuần 4; Tuần 5 bổ sung cơ chế scope/filter theo domain)
- `GET /agents/{id}/capabilities`
- JSON Schema.
- Discovery client.

**Deliverable:** Dummy Agent mới có thể discovery Agent khác mà không hard-code danh sách.

## Tuần 6 — Task Model + SignalR Relay

- Task ID.
- Caller/target.
- Capability.
- Input/output.
- Task lifecycle.
- Parent/root task.
- SignalR Hub.
- Message relay.

Kiểm thử:

```text
A → B
```

**Deliverable:** synchronous Agent-to-Agent call chạy end-to-end.

## Tuần 7 — Async Task + State

> R3: phạm vi Tuần 7 phần A đã gộp thêm rate limit, depth/cycle, thu hồi kết nối và log xác thực (ADR-W8-01…04) vì chúng dùng chung `TaskService`; Tuần 8 chỉ còn kiểm thử bảo mật và hoàn thiện policy phía Agent/QA.

- `submitted → working → completed/failed`.
- Push notification.
- Timeout/deadline.
- Redis runtime state.
- Agent disconnect/reconnect.
- Task state recovery cơ bản.

Kiểm thử:

```text
A → B
B xử lý lâu
B → completed
A nhận push
```

**Deliverable:** sync + async Task hoạt động.

## Tuần 8 — Authentication + Policy + Recursion Protection

- Hoàn thiện áp dụng API Key cho toàn bộ Task/SignalR (cơ chế credential đã có từ Tuần 4); JWT là tuỳ chọn.
- Caller identity.
- Domain namespace scoping.
- Access policy.
- Rate limiting.
- Maximum depth.
- Parent/root Task.
- Cycle protection.
- Security tests.

Kiểm thử:

```text
A → B → C
A → B → C → A
Agent domain X gọi Agent domain Y (khác domain) → bị từ chối trừ khi public/cross-domain policy cho phép
```

**Milestone:** Core Runtime hoàn chỉnh ở mức chức năng chính.

---

# Giai đoạn 3 — Reference Agents / JobHunt (Tuần 9–11)

## Tuần 9 — Reference Agent Model

Thiết kế các Agent:

```text
Job Search Agent
CV Analysis Agent
Company Research Agent
```

Mỗi Agent có:

- identity;
- capability;
- schema;
- domain = `jobhunt`.

Không xây LLM thật.

**Deliverable:** Agent Cards + capability contracts.

## Tuần 10 — Implement Dummy/Reference Agents

- Console Agent Client.
- Registration.
- Discovery.
- Task creation.
- Task receiving.
- Task response.
- Agent-to-Agent nested calls.

**Deliverable:** nhiều Agent hoạt động qua AERS.

## Tuần 11 — Recursive End-to-End Demo

Demo:

```text
A → B
A → B → C
A → B → C → D
```

và tình huống lỗi:

```text
A → B → C → A
```

Kiểm chứng AERS không tự orchestration.

**Milestone:** Demo Agent-to-Agent hoàn chỉnh.

---

# Giai đoạn 4 — Scale, Reliability & Load Test (Tuần 12–14)

## Tuần 12 — Scale-out

- Docker Compose.
- 2+ AERS instances.
- Redis shared state.
- SignalR scale-out.
- Load balancer.
- Connection recovery.

**Deliverable:** nhiều AERS instance cùng phục vụ Agent.

## Tuần 13 — Observability & Security

- Structured logging.
- Metrics.
- Tracing nếu kịp.
- Rate limiting.
- Authentication failure.
- Policy violation.
- Task timeout.
- Recursive call protection.

**Deliverable:** monitoring + security checklist.

## Tuần 14 — Load Test + Hardening

Đo:

- concurrent Agents;
- concurrent Tasks;
- latency;
- throughput;
- error rate;
- disconnect/reconnect;
- multi-instance behavior.

Test scenarios:

```text
A → B
A → B → C
many A → many B
many recursive chains
```

**Deliverable:** Load Test Report + bug fixing.

---

# Giai đoạn 5 — Tài liệu & báo cáo (Tuần 15)

- Protocol Specification v1.0.
- Architecture Document.
- API documentation.
- Deployment Guide.
- Sequence diagrams.
- Demo script.
- Video dự phòng.
- Final report.

**Milestone:** hoàn tất source + documentation + demo.

---

# Phân công 2 thành viên

## Thành viên A — Core / Infrastructure Owner

- Protocol.
- Agent Registry.
- Discovery.
- Task Manager.
- SignalR.
- Redis.
- PostgreSQL.
- Scale-out.
- Deployment.

## Thành viên B — Agent / QA / Policy Owner

- Agent Card/capability contract.
- Dummy Agent Client.
- Reference JobHunt Agents.
- Authentication/policy.
- Recursion/rate limiting.
- Integration test.
- Load test.
- API documentation.

Hai thành viên review chéo kiến trúc và protocol.

---

# Milestones

| Tuần | Milestone |
|---|---|
| 3 | Protocol + Architecture |
| 5 | Agent Registry + Discovery |
| 6 | A → B synchronous call |
| 8 | Core Runtime + security/recursion |
| 11 | Recursive Agent demo |
| 12 | Multi-instance |
| 14 | Load test + hardening |
| 15 | Final report/demo |

---

# Rủi ro

| Rủi ro | Phương án |
|---|---|
| Protocol quá phức tạp | Chốt MVP Task model trước |
| AERS vô tình trở thành orchestrator | Quy định rõ server chỉ relay/policy |
| Recursive call tạo vòng lặp | Depth + timeout + ancestry + cycle protection |
| SignalR scale-out khó | Redis backplane, Docker Compose |
| Agent registry phức tạp | PostgreSQL + metadata đơn giản |
| Agent Card quá phức tạp | Chỉ mô tả capability cần thiết cho MVP |
| 2 người quá tải | Ưu tiên Registry → Discovery → Task → Relay trước |
| Observability không kịp | Structured logging trước, tracing/metrics sau |
| Kubernetes không kịp | Docker Compose là baseline |

---

# MVP bắt buộc

Nếu tiến độ gặp vấn đề, phải bảo vệ được chuỗi:

```text
Register Agent
      ↓
Discover Agent
      ↓
Create Task
      ↓
Authenticate
      ↓
Policy Check
      ↓
Relay
      ↓
Target Agent
      ↓
Result
      ↓
Caller Agent
```

Sau đó mới ưu tiên:

```text
Async
→ Recursive
→ Redis
→ Multi-instance
→ Load Test
→ Observability
```

---

# Lịch sử sửa đổi

| Revision | Nội dung |
|---|---|
| R1 | Bản v2. |
| R2 | Đồng bộ trước Tuần 4: bổ sung phạm vi Tuần 4 (credential, xoá mềm, validation, `GET /agents/{id}`); điều chỉnh Tuần 5 và Tuần 8 tương ứng; sửa tham chiếu tên tệp. Kế hoạch triển khai chi tiết Tuần 4 nằm ở `.ai/week-04/week-04-tasks.md`. |
| R3 | Đồng bộ Tuần 7: ghi chú gộp phạm vi Tuần 7 và Tuần 8 (phần A). |

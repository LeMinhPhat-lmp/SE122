# AERS — Decision Log (Sổ ghi nhận quyết định thiết kế)

**Đề tài:** Phát triển hệ thống backend cho Agent-to-Agent Runtime (AERS)
**Revision:** R3 — đồng bộ Tuần 7 (R2 lập trước Tuần 4). Tài liệu thay thế `05-schema-index-and-decisions.md` (đã chuyển vào `archive/`).
**Mục đích:** tập trung hoá các quyết định thiết kế đã được đưa ra, trạng thái xác nhận, căn cứ và tài liệu chịu ảnh hưởng, nhằm tránh việc quyết định bị phân tán qua nhiều tài liệu.

## 1. Quy ước trạng thái

| Trạng thái | Ý nghĩa |
|---|---|
| **Approved** | Đã được nhóm thống nhất; có hiệu lực bắt buộc đối với triển khai. |
| **Approved (uỷ quyền)** | Nhóm uỷ quyền cho bên soạn thảo đề xuất cách giải quyết; nội dung đề xuất có hiệu lực như Approved cho đến khi có thay đổi. |
| **Adopted R2** | Được đưa vào Revision R2 của bộ tài liệu như mặc định để triển khai Tuần 4; chưa có xác nhận riêng và có thể điều chỉnh bằng một revision sau. |
| **Open** | Chưa quyết định; không được triển khai theo một hướng cụ thể trước khi chốt. |

## 2. Quyết định đã chốt (Approved)

| ID | Quyết định | Căn cứ / Lý do | Tài liệu chịu ảnh hưởng |
|---|---|---|---|
| D-01 | `agentId` là UUID v4 do AERS sinh tại thời điểm đăng ký, duy nhất trong phạm vi một AERS instance/cluster (kể cả khác domain). Agent Developer không tự đặt `agentId`; payload đăng ký chứa `agentId` bị từ chối với `VALIDATION_ERROR`. Wire type là `string`; việc kiểm tra UUID v4 thực hiện ở tầng service (`Guid` + kiểm tra version/variant), không dùng regex trong schema. | Tránh trùng định danh, loại bỏ khả năng giả mạo identity qua payload; tránh hai nơi phải đồng bộ logic định dạng. | Spec 1.2–1.4; Mô tả đề tài 9 |
| D-02 | Toàn bộ schema dùng JSON Schema Draft 2020-12; thư viện validate là `JsonSchema.Net`. | Hỗ trợ Draft 2020-12 đầy đủ hơn các thư viện hướng draft cũ. | Spec; Tech Stack A.3 |
| D-03 | Ba lớp model không đồng nhất: Wire Protocol → Application DTO → Persistence Entity. | Cho phép thay đổi từng lớp độc lập. | Spec (phần mở đầu); Design Diagrams D3, D5 |
| D-04 | Xác thực MVP bằng API Key gồm hai loại credential: registration key (Operator cấp, header `X-Registration-Key`, chỉ cho `POST /agents/register`) và Agent API key (AERS cấp khi đăng ký, header `X-Api-Key`; 256-bit CSPRNG; chỉ lưu SHA-256; trả một lần). `agentId` của caller luôn suy ra từ credential. JWT không triển khai trong MVP. Không có rotate/recover key. | Nhóm uỷ quyền bên soạn thảo giải quyết. Khoảng trống ban đầu: endpoint đăng ký yêu cầu xác thực nhưng Agent chưa có identity, và response chỉ trả `agentId`. Giải pháp tối thiểu, không cần quản lý người dùng/Tenant. | Spec 1.4, 8; Tech Stack A.4; Mô tả đề tài 7, 15 |
| D-05 | Deregistration là xoá mềm (`deregistered_at`). Sau deregister: không còn trong discovery; `GET /agents/{id}` trả 404; không nhận Task mới; credential bị thu hồi; Task đang chạy không bị huỷ cưỡng bức. | Bảo toàn tham chiếu từ Task/Audit; audit phải tồn tại lâu hơn Agent. | Spec 2.2; Tech Stack A.7; Design Diagrams D6, D9 |
| D-06 | Trạng thái online/offline không lưu trong PostgreSQL; suy ra từ Redis key `aers:agent:{id}:alive` (TTL 45 giây, heartbeat 15 giây), không dùng background worker. | Giảm tải ghi DB; TTL tự dọn. | Spec 2.1 |
| D-07 | `ancestorChain_new = ancestorChain_parent + [targetAgentId của parent]`; caller chỉ gửi `parentTaskId`; `ancestorChain` là state phía server. Trường hợp cơ sở (Adopted R2): root Task có `ancestorChain = [callerAgentId]`. | Công thức do nhóm chốt. Trường hợp cơ sở được bổ sung vì nếu root bắt đầu bằng chuỗi rỗng thì vòng A→B→C→A không bị phát hiện (A ∉ `[B, C]`), trái với Success Criteria SC-05. | Spec 4; Sequence Diagrams 3; Design Diagrams D12 |
| D-08 | `Domain` là namespace dạng chuỗi có index; không có bảng `domains`/`tenants`. `ownerRef` là cột nullable giữ chỗ cho Tenant, không có hành vi trong MVP. | Tenant là định hướng tương lai, không thuộc phạm vi 15 tuần. | Mô tả đề tài 7; Tech Stack A.7 |
| D-09 | `PolicyContext.additionalProperties = true` có chủ đích; các schema còn lại `additionalProperties = false`. | Cho phép mở rộng điều kiện policy mà không phá protocol. | Spec 9.1 |
| D-10 | `error.code` là `string` tự do, không enum. | Tránh breaking change khi bổ sung mã. | Spec 12 |
| D-11 | Các tham số vận hành (heartbeat 15/45 giây, rate limit 100/60 giây, max depth 5, timeout 300 giây) cấu hình qua `appsettings`, không hard-code. | Hiệu chỉnh được sau Load Test Tuần 14. | Spec 9.3 |

### Các quyết định của Revision R1 được duy trì

| ID | Quyết định | Tham chiếu |
|---|---|---|
| D-12 | `TaskEnvelope` là discriminated union theo `messageType` (`task.create`, `task.invoke`, `task.result`, `task.status_event`) với trường `payload`; `correlationId` tuỳ chọn. | Spec 6.2 |
| D-13 | `TaskInvocation` là payload riêng AERS → Target Agent; không chứa `ancestorChain`. | Spec 6.3 |
| D-14 | `CycleCheckContext` là model nội bộ, không thuộc wire protocol; kiểm tra ở cấp Agent (`AgentOnly`). | Spec 4 |
| D-15 | Registration response: HTTP 201 (R2 bổ sung `apiKey`). | Spec 1.4 |
| D-16 | Deregistration request body rỗng (`maxProperties: 0`). | Spec 2.2 |
| D-17 | Rate limit: fixed window counter theo `callerAgentId` qua Redis `INCR` + `EXPIRE`; 100 `task.create` / 60 giây. | Spec 10 |
| D-18 | Deadline/timeout chỉ đặt ở `TaskRuntimeState`, không đưa vào `Task`/`TaskInvocation` gửi cho Agent; Redis key `aers:task:{taskId}`; TTL theo `deadlineAt`. | Spec 6.4, 11 |
| D-19 | Cross-domain: dùng `callerDomain`/`targetDomain` trong `PolicyContext`; allowlist cấu hình (`DenyByDefault`). | Spec 9.1, 9.3 |
| D-20 | Audit `eventType` baseline gồm năm loại: `agent.registered`, `agent.deregistered`, `task.created`, `task.status_changed`, `policy.rejected`; heartbeat không ghi audit. | Spec 6.5 |
| D-21 | MVP dùng safety-first waiting semantics; fire-and-forget là giới hạn được chấp nhận. | Spec 4 |

## 3. Quyết định được đưa vào Revision R2 (Adopted R2)

Các mục dưới đây được lập trong quá trình đồng bộ trước Tuần 4. Mỗi mục có thể điều chỉnh bằng một revision sau nếu nhóm không đồng ý.

| ID | Nội dung | Lý do | Tài liệu chịu ảnh hưởng |
|---|---|---|---|
| A1 | `GET /agents/{id}` triển khai từ Tuần 4; response là `AgentDetailResponse { agentCard, runtimeStatus }` (schema mới). | Cần cách quan sát trạng thái online/offline; `AgentCard` có `additionalProperties:false` nên không thể chứa trạng thái runtime. | Spec 1.7; Kế hoạch (Tuần 4–5) |
| A2 | Quyền đọc `GET /agents/{id}`: chính Agent đó, cùng domain, hoặc cặp domain nằm trong allowlist; còn lại `403 DOMAIN_FORBIDDEN`. Thứ tự: 404 trước 403. | Không để lộ dữ liệu cross-domain khi chưa có allowlist (`DenyByDefault`). | Spec 1.8 |
| A3 | Mã lỗi mới `AGENT_ACCESS_FORBIDDEN` (HTTP 403) khi Agent heartbeat/deregister thay Agent khác. | Spec chưa có mã cho tình huống này; `code` là chuỗi mở nên không breaking. | Spec 12.2 |
| A4 | Giới hạn đầu vào đăng ký: `name` 1–200; `domain` `^[a-z0-9][a-z0-9-]{0,63}$` (chuẩn hoá); 1–50 capability; tên capability `^[A-Za-z][A-Za-z0-9_.-]{0,99}$` và duy nhất trong Agent; mỗi schema ≤ 64 KiB và hợp lệ theo Draft 2020-12; body ≤ 1 MiB. | Bảo vệ hạ tầng khỏi payload bất thường; Spec trước đó chưa nêu. | Spec 1.2.1, 9.3 |
| A5 | Đăng ký thành công đánh dấu Agent online ngay; ghi Redis sau commit DB theo nguyên tắc best-effort (lỗi Redis không làm thất bại đăng ký). Heartbeat và `GET /agents/{id}` gặp lỗi Redis trả `500 INTERNAL_ERROR`. | Tránh trạng thái DB đã commit nhưng client nhận lỗi. | Spec 1.4, 2.1 |
| A6 | Heartbeat bỏ qua `timestamp` do client gửi (chỉ kiểm tra định dạng); dùng đồng hồ server. Bổ sung key `aers:agent:{id}:lastbeat` (TTL 24 giờ) để trả `lastHeartbeatAt`. | Không tin đồng hồ client; Spec chưa nêu nơi lưu `lastHeartbeatAt`. | Spec 2.1 |
| A7 | `DELETE /agents/{id}` yêu cầu body rỗng hoặc `{}`; thành công `204`. | Khớp `deregistration-request.schema`. | Spec 2.2 |
| A8 | Migration của Tuần 4 chỉ tạo bảng `agents`, `agent_capabilities`, `agent_credentials`, `audit_events`; `tasks`, `task_status_history` thêm ở Tuần 6. | Tránh dựng schema chưa sử dụng. | `.ai/week-04/week-04-tasks.md` |
| A9 | Migration tự động chỉ ở môi trường Development; môi trường khác áp dụng bằng lệnh riêng. | Tránh nhiều instance migrate đồng thời. | Tech Stack A.7 |
| A10 | Registration key đọc từ cấu hình `Aers:Auth:RegistrationKeys`; so sánh theo thời gian hằng số; ứng dụng từ chối khởi động nếu danh sách rỗng ngoài Development. | Không có "cửa mở" mặc định. | Spec 8.3, 9.3 |
| OD-5 | Bổ sung bước kiểm tra `input` theo `inputSchema` của capability vào thứ tự Policy (sau "Capability exists", trước "Rate limit"), lỗi `VALIDATION_ERROR`. | Phục vụ SC-02 và hợp đồng capability; Spec 9.4 trước đó không liệt kê. | Spec 9.4; Design Diagrams D14 |
| OD-6 | Persistence Entity của Task lưu thêm `ancestorChain` và `deadlineAt`; hai giá trị này không thuộc wire protocol. | Spec đã yêu cầu AERS "tính và lưu" `ancestorChain`; Timeout worker và truy vấn sau khi TTL Redis hết cần dữ liệu bền vững. | Spec 3.2; Design Diagrams D4, D6 |
| OD-7 | Loại bỏ định danh minh hoạ `cv-agent-001` và `agentId` khỏi payload đăng ký trong toàn bộ bộ tài liệu chuẩn. | Nhất quán với D-01. | Mô tả đề tài; Tổng quan component; Sequence Diagrams |
| OD-8 | Thống nhất tham chiếu tên tệp: hậu tố `-v3` là chỉ số bộ tài liệu; protocol giữ nhãn phiên bản `v1.0`. | Tên tệp và nhãn nội dung trước đó không khớp. | Toàn bộ bộ tài liệu |

## 3A. Quyết định Tuần 7 (Accepted)

Các ADR dưới đây (`.ai/week-07/ADR-decisions`) do Lê Minh Phát (thành viên A) quyết định; trạng thái ghi trong ADR chuyển từ Proposed sang Accepted. Tài liệu chịu ảnh hưởng: Protocol Specification R3, Sequence Diagrams R3, Design Diagrams R3.

| ADR | Quyết định | Lý do | Tài liệu chịu ảnh hưởng |
|---|---|---|---|
| ADR-W7-01 | `TaskRuntimeState` trong Redis chỉ ghi; PostgreSQL là nguồn sự thật. Ghi sau mỗi commit thắng compare-and-set; lỗi Redis chỉ log. | Tránh hai nguồn sự thật; Redis dùng cho tuần 12 và debug. | Spec 6.4; Design D7 |
| ADR-W7-02 | Không có kho "chưa giao" trong Redis. Thêm `tasks.acknowledged_at` và message `task.ack`; khi Agent nối lại, gửi lại Task terminal chưa ack (ít nhất một lần, tối đa 100 mỗi lần nối). Push chỉ best-effort. | Một nguồn dữ liệu bền vững; client chủ động xác nhận thay vì suy đoán "đã giao". | Spec 3.2, 6.2, 6.6; Design D4, D6, D13 |
| ADR-W7-03 | Mất kết nối target: ghi dấu thời điểm ngắt trong Redis sorted set; sau ân hạn 30 giây mà chưa nối lại, worker fail Task `working` với `TARGET_UNAVAILABLE`. | Target có thể nối lại và vẫn trả kết quả; vẫn không để caller chờ đến hết deadline. | Spec 3.1, 11.2; Design D7, D13 |
| ADR-W7-04 | Chỉ chặn deadline Task con ≤ deadline Task cha (`min(now + timeout, deadline cha)`). | Tránh con sống lâu hơn cha mà không thêm cơ chế huỷ dây chuyền. | Spec 11.1 |
| ADR-W8-01 | Rate limit chạy ngay sau khi caller hợp lệ và trước khi tra target; Redis lỗi thì fail-closed (`INTERNAL_ERROR`, `retryable: true`). | Request bị từ chối sớm không tốn truy vấn DB; fail-closed an toàn hơn cho hạ tầng dùng chung. | Spec 9.4, 10; Design D14 |
| ADR-W8-02 | Không chặn root Task do Agent đang là target của Task `working`; chấp nhận hạn chế, rate limit chặn gián tiếp, audit `task.created` mang cờ `orphanSuspect`. | Không có cách hạ tầng xác minh Agent có bỏ `parentTaskId` hay không; không đưa ngữ nghĩa nghiệp vụ vào AERS. | Spec 6.5 |
| ADR-W8-03 | Deregister publish `agentId` lên Redis pub/sub `{prefix}agent:revoked`; mọi instance subscribe và abort kết nối đang giữ; mọi message còn kiểm tra Agent gửi vẫn active. | Kết nối SignalR có thể sống lâu hơn credential; multi-instance cần thông báo chéo. | Spec 8.5; Design D7, D9 |
| ADR-W8-04 | Log cảnh báo lỗi xác thực có cấu trúc, tối đa một log mỗi 10 giây cho mỗi (địa chỉ, lý do), tối đa 10.000 mục; không ghi DB, không ghi credential. | Chống làm đầy log/DB khi bị tấn công dò key. | Spec 8.5 |
| ADR-W9-02 | Kiểm tra `output` theo `outputSchema` chỉ ghi log cảnh báo; Task vẫn `completed`. | Không biến AERS thành bộ kiểm duyệt kết quả; vẫn có tín hiệu cho vận hành. | Spec (mục 5.2 không đổi) |

Kết quả cho các mục Open: OP-2 — key rate limit chốt là `{prefix}ratelimit:{callerAgentId}:{windowStart}` và ánh xạ kết nối là `{prefix}conn:{agentId}` (đã triển khai Tuần 6). OP-3 — ánh xạ HTTP chốt trong Spec 12.3 (`DEPTH_EXCEEDED`, `CYCLE_DETECTED`, `TASK_STATE_CONFLICT` 409; `TARGET_UNAVAILABLE` 503; `TASK_TIMEOUT` chỉ trong `task.result`).

## 4. Vấn đề còn mở (Open)

| ID | Vấn đề | Đề xuất tạm thời | Thời điểm cần chốt |
|---|---|---|---|
| OD-3 | Khi Policy từ chối, có tạo Task ở trạng thái `failed` hay không. Spec 3.1 và state diagram cho phép `submitted → failed`; lifecycle định nghĩa Task `submitted` sau khi Policy qua. | Không tạo Task; chỉ trả lỗi và ghi audit `policy.rejected`. | Trước Tuần 6 |
| OP-1 | Cách truyền Agent API key tại bước thiết lập kết nối SignalR. | Cùng key `X-Api-Key`; cơ chế truyền cụ thể theo khả năng của client. | Tuần 6 |
| OP-2 | Định dạng key Redis cho rate limit và ánh xạ `agentId → connectionId`. | `aers:ratelimit:{callerAgentId}:{windowStart}`; `aers:conn:{agentId}`. | Tuần 6–8 |
| OP-3 | Ánh xạ HTTP status cho các mã `DEPTH_EXCEEDED`, `CYCLE_DETECTED`, `TARGET_UNAVAILABLE`, `TASK_TIMEOUT` khi phát sinh ở endpoint REST. | Chốt cùng Task Manager. | Tuần 6–8 |
| OP-4 | Cơ chế xoay vòng/khôi phục Agent API key; hỗ trợ JWT. | Ngoài phạm vi MVP. | Sau MVP |

## 5. Lịch sử sửa đổi

| Revision | Nội dung |
|---|---|
| R2 | Lập tài liệu; hợp nhất các quyết định từ `05-schema-index-and-decisions.md` (archive), Protocol Specification R1 và các quyết định trước Tuần 4; bổ sung các mục Adopted R2 và Open. |
| R3 | Đồng bộ Tuần 7: thêm mục 3A (chín ADR Accepted); cập nhật OP-2 và OP-3. |

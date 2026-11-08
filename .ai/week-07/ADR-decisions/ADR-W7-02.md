# ADR-W7-02: Đảm bảo caller offline nhận được kết quả: lưu `acknowledged_at` trong PostgreSQL

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W7-02 |
| **Status** | Proposed |
| **Ngày** | 2026-11-06 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-03, A-04, A-07, B-03, B-04, B-05 |
| **Thay thế / liên quan** | Không (liên quan ADR-W7-03 và ADR-W8-03) |

## 1. Bối cảnh

Kết quả được push cho caller qua SignalR theo kiểu best-effort. Nếu caller offline vào lúc đó thì push bị mất; hiện cách duy nhất để lấy lại là caller tự gọi `GET /tasks/{id}`, nghĩa là caller phải tự nhớ những Task đang chờ.

Contract (A48, A49) đề xuất lưu `taskId` chưa giao trong tập Redis (TTL 24 giờ) và giao lại khi caller kết nối. Cách này không bền vững (Redis mất hoặc caller kết nối lại sau TTL thì dữ liệu mất) và không có xác nhận từ client nên AERS không biết kết quả đã thực sự được nhận hay chưa.

## 2. Quyết định

Chọn phương án 3, điều chỉnh: lưu trạng thái giao kết quả trong PostgreSQL bằng cột `acknowledged_at` (thay cho `delivered_at` ở đề xuất ban đầu), do client chủ động xác nhận.

- **Dữ liệu:** thêm cột `tasks.acknowledged_at timestamptz NULL` (migration mới, thực hiện trong A-03) và index một phần `ix_tasks_caller_unacknowledged` trên `(caller_agent_id, completed_at)` với điều kiện `status IN (2, 3) AND acknowledged_at IS NULL`. Cột này chỉ thuộc lớp persistence, không lộ ra `GET /tasks/{id}`.
- **Xác nhận:** thêm message client → server `task.ack` với payload `{ "taskId": "<guid>" }` (`additionalProperties: false`). Caller gửi sau khi đã xử lý `task.result`. Server đặt `acknowledged_at = now` bằng một lệnh UPDATE có điều kiện (`WHERE id = @id AND caller_agent_id = @sender AND acknowledged_at IS NULL`); thao tác idempotent, không tạo history hay audit, không tính vào rate limit. Task không tồn tại → `TASK_NOT_FOUND`; sender không phải caller → `TASK_ACCESS_FORBIDDEN`; Task chưa ở trạng thái kết thúc → `TASK_STATE_CONFLICT`; ack thành công (kể cả ack lặp) → `accepted: true`.
- **Giao lại khi kết nối:** sau khi hub bind kết nối, `ITaskRecoveryService.RedeliverAsync(agentId)` chạy ngoài đường connect (scope DI mới, như A49). Hàm này đọc PostgreSQL tối đa `RedeliveryBatchSize` (100) Task kết thúc của caller có `acknowledged_at IS NULL`, sắp theo `completed_at, id`, và push `task.result` giống hệt push trực tiếp. Push lỗi hoặc mất kết nối thì dừng; không cần hoàn tác vì không có dữ liệu nào bị xóa.
- **Push trực tiếp:** `ITaskNotifier.NotifyResultAsync` chỉ push best-effort. Caller không có kết nối hoặc gửi lỗi thì ghi log và không lưu thêm gì; Task vẫn có `acknowledged_at IS NULL` nên sẽ được giao lại ở lần kết nối sau.
- **Ngữ nghĩa:** giao ít nhất một lần (at-least-once). Kết quả có thể đến nhiều lần cho đến khi caller ack; client khử trùng theo `taskId` và ack mỗi lần nhận.
- **Không làm:** tập Redis `undelivered:{callerId}`, `IUndeliveredResultStore`, option `UndeliveredTtlHours`; giao lại `task.status_event` hoặc Task `working`; giao nhiều batch trong một lần kết nối; giới hạn thời gian giao lại (Task chưa ack được giao lại cho đến khi ack).

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Giao lại at-least-once bằng Redis: khi push thất bại, ghi `taskId` vào tập `undelivered:{callerId}` (TTL 24 giờ); khi caller kết nối lại, AERS lấy tập đó (atomic) và push lại `task.result` | Trải nghiệm tốt, đúng tinh thần "push" | Thêm độ phức tạp cho A-03/A-04; client phải idempotent; có khe mất dữ liệu hẹp nếu process chết giữa lúc lấy và gửi | Không chọn; vì dữ liệu không bền vững (hết TTL hoặc Redis mất thì mất kết quả), không có ACK nên không biết caller đã nhận, và làm hệ thống phức tạp hơn mức cần thiết |
| 2 | Chỉ dùng `GET /tasks/{id}` (client tự polling khi nối lại) | Đơn giản (bớt khoảng 4 giờ); không thêm state Redis | Client phải tự lưu danh sách Task đang chờ | Không chọn; vì đẩy trách nhiệm theo dõi Task đang chờ sang client |
| 3 | Lưu trạng thái giao trong PostgreSQL và giao lại các Task chưa giao khi kết nối lại. Điều chỉnh: dùng `acknowledged_at` (client xác nhận) thay vì `delivered_at` | Bền vững, không phụ thuộc Redis hay TTL; biết chắc client đã nhận | Cần migration, thêm message `task.ack`, thêm một lần ghi DB cho mỗi kết quả | Chọn; vì đảm bảo bền vững khi Redis mất hoặc caller chỉ kết nối lại sau nhiều giờ. Dùng `acknowledged_at` vì `delivered_at` chỉ chứng minh server đã gửi, không chứng minh client đã nhận |

## 4. Hệ quả

- **Tích cực:** Kết quả không mất khi Redis mất hoặc caller kết nối lại muộn; có xác nhận từ client nên biết kết quả đã được nhận; Redis không còn giữ dữ liệu cần bền vững.
- **Tiêu cực / rủi ro chấp nhận:** Thêm migration và một message `task.ack` vào wire protocol; mỗi kết quả tốn thêm một lần ghi DB; client phải idempotent và phải gửi ack; client không gửi ack (ví dụ HubProbe bản cũ) sẽ nhận lại cùng kết quả ở mỗi lần kết nối.
- **Cần làm thêm / theo dõi:**
  - Cập nhật `AERS.AgentClient` (B-03) tự gửi `task.ack` sau khi xử lý kết quả; thêm lệnh ack cho HubProbe (hướng dẫn kiểm thử thủ công ở A-08).
  - Xem lại ở Tuần 12–14: theo dõi số Task chưa ack; nếu tăng bất thường thì cân nhắc cửa sổ thời gian hoặc giới hạn số lần giao lại.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** Kết quả được giao ít nhất một lần và có thể trùng; mỗi lần kết nối giao tối đa 100 kết quả chưa ack, phần còn lại giao ở lần kết nối sau hoặc lấy qua `GET /tasks/{id}`; `task.invoke` và `task.status_event` không bao giờ được giao lại.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-03 | Bỏ `IUndeliveredResultStore` và cài đặt Redis; `TaskNotifier` chỉ push và log, không phụ thuộc Redis; giữ `ITaskNotifier` vì vẫn cần push kết quả và trạng thái `working`; refactor `TaskMessageService` để không phụ thuộc Redis; thêm xử lý `task.ack` (client → server) vào `HandleAsync`; thêm migration `acknowledged_at`; giữ hướng server → client cho `task.result`/`task.status_event` để phân biệt hướng message |
| Task A-04 | `ITaskRecoveryService.RedeliverAsync` truy vấn PostgreSQL thay vì lấy tập Redis; giữ hook reconnect của Hub (chạy ngoài đường connect, scope DI mới) vì cơ chế recovery không đổi; giữ việc chấp nhận `task.result` của target sau khi reconnect; ngữ nghĩa disconnect theo ADR-W7-03 |
| Task A-07 | Dòng "notifier/redelivery" trong ma trận lỗi: push lỗi chỉ log; truy vấn DB lỗi khi giao lại thì log warning và thử lại ở lần kết nối sau |
| Task B-03 | Client tự gửi `task.ack` sau khi xử lý `task.result`, khử trùng theo `taskId` |
| Task B-04 | Scenario `caller-offline`: caller ack ở lần nhận đầu nên không bị giao lại; vẫn chấp nhận tối đa 2 lần nhận nhưng chỉ xử lý một lần |
| Task B-05 | Đổi tests 21–27: 21 caller offline, B trả lời → Task kết thúc và `acknowledged_at IS NULL`; 22 reconnect → nhận đúng `task.result`, gửi `task.ack` → `acknowledged_at` được đặt; 23 caller luôn online nhận một lần, ack, các lần reconnect sau không nhận lại; 24 timeout lúc caller offline → reconnect nhận `failed/TASK_TIMEOUT`; 25 ack Task `working` → `TASK_STATE_CONFLICT`; 26 ack Task của caller khác → `TASK_ACCESS_FORBIDDEN`; 27 có 500 Task chưa ack, batch 100 → nhận 100, 400 Task còn lại vẫn chưa ack, `StartAsync` không bị chặn |
| Spec / Decision Log / diagram | Spec: thêm `task.ack` vào TaskEnvelope (enum và `oneOf`) cùng schema `task-ack`; ghi `acknowledged_at` là trường chỉ thuộc persistence (mục 3.2); thay mô tả undelivered/redelivery (A48, A49). Decision Log: thay A48, A49. Diagram: D6 (cột mới), D7 (bỏ key `undelivered`), D13 và `Protocol-Sequence-Diagrams` mục 5 (giao lại từ PostgreSQL và bước ack). Kế hoạch: ghi nhận phạm vi |
| Contract (`01-shared-contract.md`) | Viết lại A48, A49 theo ADR này (bỏ thẻ `[ADR-W7-02]`); thêm `task.ack` vào mục 1.3; bỏ `IUndeliveredResultStore` khỏi mục 1.5 và `UndeliveredTtlHours` khỏi A51/1.6. Đây là ngoại lệ có chủ đích của quy tắc đóng băng (contract mục 1.0, điểm 3) vì cơ chế lưu chuyển sang PostgreSQL; chữ ký `ITaskNotifier` và `ITaskRecoveryService` giữ nguyên. Sửa contract trước khi giao A-03 (README mục 11) |

## 6. Cách kiểm chứng

- **Unit test:** `TaskNotifier` không ném lỗi khi không có kết nối hoặc send lỗi và không ghi gì; `TaskRecoveryService` chỉ giao Task kết thúc, chưa ack, đúng caller, tôn trọng batch limit, dừng khi send lỗi; xử lý `task.ack` idempotent và trả đúng ba mã lỗi.
- **Integration test:** IT-W7-21 … IT-W7-27 theo bảng ở mục 5.
- **Thủ công:**
  1. A tạo Task chậm rồi ngắt kết nối; B trả lời.
  2. Chạy `SELECT id, status, acknowledged_at FROM tasks WHERE caller_agent_id = '<A>';` → `acknowledged_at` là NULL.
  3. A kết nối lại → nhận `task.result` trong khoảng 1 giây; gửi ack (HubProbe `ack <taskId>`) → `acknowledged_at` có giá trị.
  4. Kết nối lại lần nữa → không nhận lại kết quả.
  5. `redis-cli --scan --pattern 'aers:undelivered:*'` trả về rỗng.

## 7. Ghi chú

Điểm cần xác nhận trước khi chuyển sang `Accepted`: tên message `task.ack` và việc sửa contract (bỏ `IUndeliveredResultStore`) trước khi giao A-03 và B-03. Điều kiện xem lại: số Task chưa ack tăng bất thường (client không ack), hoặc truy vấn giao lại khi kết nối trở thành điểm nghẽn ở Tuần 12–14.

---

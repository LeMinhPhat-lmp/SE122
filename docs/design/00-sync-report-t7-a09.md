# Báo cáo đồng bộ tài liệu — Task A-09 (Tuần 7)

**Phạm vi:** đồng bộ bộ tài liệu thiết kế sau khi triển khai phần A của Tuần 7 (A-01…A-08) theo chín ADR đã Accepted.
**Kết quả:** Revision R3 của các tài liệu bên dưới. Tài liệu không liệt kê ở đây không thay đổi.

## 1. Thay đổi theo từng tệp

| Tệp | Thay đổi | Mục |
|---|---|---|
| `Protocol-Specification-v3.md` | Thêm `task.ack` (enum, `oneOf`, schema `TaskAck`) và mục 6.6 (push, ack, gửi lại ít nhất một lần) | 6.2, 6.6, Phụ lục |
| | `acknowledgedAt` chỉ thuộc persistence; bảng chuyển trạng thái do timeout / mất kết nối; `TASK_STATE_CONFLICT` cho kết quả đến muộn | 3.1, 3.2 |
| | Ngữ nghĩa `depth > MaxRecursionDepth` | 4 |
| | Runtime state chỉ ghi, TTL, Lua nguyên tử; cờ `orphanSuspect` ở audit `task.created` | 6.4, 6.5 |
| | Log lỗi xác thực có giới hạn tần suất; thu hồi kết nối qua Redis pub/sub; kiểm tra Agent active ở mọi message | 8.5 |
| | Tham số `Tasks.*` mới; thứ tự Policy mới (rate limit sớm); key rate limit, fail-closed | 9.3, 9.4, 10 |
| | Deadline Task con, quét timeout nhiều instance, ân hạn mất kết nối | 11 |
| | Ánh xạ HTTP đầy đủ và cấu trúc `details`/`retryable` của `RATE_LIMITED`, `DEPTH_EXCEEDED`, `CYCLE_DETECTED` | 12.3 |
| `05-decision-log-v3.md` | Mục 3A: chín ADR Accepted; cập nhật kết quả OP-2, OP-3 | 3A |
| `Protocol-Sequence-Diagrams-v3.md` | Thêm sequence timeout worker, task.ack và gửi lại, ân hạn mất kết nối | 7, 8, 9 |
| `AERS-Design-Diagrams-v3.md` | D4, D6 (`acknowledged_at`, index một phần); D7 (`aers:disconnected`, kênh `aers:agent:revoked`); D13; D14 viết lại | D4, D6, D7, D13, D14 |
| `01-ke-hoach-15-tuan-v3.md` | Ghi chú gộp phạm vi Tuần 7 và Tuần 8 phần A | Tuần 7 |

## 2. Không thay đổi

`02-tech-stack-v3.md`, `03-mo-ta-de-tai-v3.md`, `04-tong-quan-component-va-tham-khao-v3.md` và Project Charter: nội dung ở mức tổng quan không mâu thuẫn với R3 (xem Spec để biết chi tiết). Trạng thái ADR đã đổi từ Proposed sang Accepted.

## 3. Điểm cần nhóm xác nhận

1. Phần mã A-06 dùng `IRateLimiter` và `EvaluateRecursion` thuộc task B-01/B-02; bản của A được kèm theo để A-06 chạy được. Nếu B đã có bản riêng, giữ bản của B (chữ ký giống nhau).
2. `IConnectionRegistry.UnbindAsync` đổi kiểu trả về từ `Task` sang `Task<bool>`.
3. ADR-W8-05, W9-01, W9-03 không có trong thư mục ADR; chỉ ảnh hưởng phần B.
4. Chưa có migration EF cho bảng `tasks`/`acknowledged_at` trong repo; phải sinh bằng `dotnet ef` (xem `docs/week-07-implementation.md`).

## 4. Kiểm tra sau đồng bộ

| Hạng mục | Kết quả |
|---|---|
| Khối JSON trong Spec parse được | Xem lệnh kiểm tra bên dưới; cần chạy lại nếu sửa tay |
| Render PlantUML | **Chưa thực hiện** (không có môi trường render); kiểm tra bằng PlantUML trước khi dùng cho báo cáo |
| Biên dịch và chạy test mã nguồn | **Chưa thực hiện** (không có .NET SDK trong môi trường soạn thảo) |

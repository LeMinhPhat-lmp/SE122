# ADR-W7-01: Vai trò của Redis `TaskRuntimeState`: chỉ ghi, không đọc

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W7-01 |
| **Status** | Proposed |
| **Ngày** | 2026-11-06 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-01, A-02, B-05 |
| **Thay thế / liên quan** | Không |

## 1. Bối cảnh

Kế hoạch Tuần 7 có mục "Redis runtime state" và Spec 6.4 định nghĩa hash `aers:task:{taskId}` (status, deadline, connectionId, instanceId, TTL). Hiện tại `GET /tasks/{id}` đọc PostgreSQL (A33), trong khi Spec 7.3 lại ghi "đọc Redis, fallback PostgreSQL". Hash này không chứa `output` và `error`, nên không thể dùng để trả lời đầy đủ `GET /tasks/{id}`.

Cần quyết định Redis `TaskRuntimeState` giữ vai trò gì: là nguồn đọc, chỉ để ghi, hay bỏ hẳn.

## 2. Quyết định

Chọn phương án 1: Redis `TaskRuntimeState` chỉ ghi; PostgreSQL vẫn là nguồn sự thật duy nhất.

- Sau mỗi lần commit tạo Task và mỗi lần commit chuyển trạng thái, ghi hash `{KeyPrefix}task:{taskId}` gồm sáu trường theo A43, TTL theo A44. Việc ghi là best-effort: lỗi Redis chỉ ghi warning, không đổi kết quả của thao tác đã commit.
- Không có code production nào đọc hash này. `GET /tasks/{id}` tiếp tục chỉ đọc PostgreSQL (A33). `ITaskRuntimeStateStore.GetAsync` chỉ phục vụ test và Tuần 12.
- Không làm: dùng Redis làm nguồn đọc, đồng bộ ngược Redis sang PostgreSQL, hoặc lưu `output`/`error` vào hash.

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Chỉ ghi sau mỗi lần commit (best-effort), không code production nào đọc; PostgreSQL vẫn là nguồn sự thật | Khớp kế hoạch và Spec 6.4; chi phí thấp; chuẩn bị sẵn `instanceId`/`connectionId` cho đa instance ở Tuần 12 | Trong Tuần 7 dữ liệu được ghi chưa có nơi tiêu thụ | Chọn; vì khớp kế hoạch, chi phí thấp và không tạo nguồn sự thật thứ hai |
| 2 | Bỏ hoàn toàn (không làm A-01) | Ít code (khoảng 3 giờ) | Lệch kế hoạch và Spec 6.4; phải làm lại ở Tuần 12 khi cần `instanceId`/`connectionId` | Không chọn; vì chỉ dời chi phí sang Tuần 12 và buộc phải sửa lại bộ tài liệu |
| 3 | Đọc Redis trước cho `GET /tasks/{id}`, fallback PostgreSQL | Đọc nhanh | Hai nguồn sự thật dễ lệch nhau (ghi Redis là best-effort nên có thể cũ hoặc thiếu); hash không có `output`/`error` nên vẫn phải đọc PostgreSQL; phức tạp hơn | Không chọn; vì Redis không phải nơi lưu dữ liệu bền vững và hash không đủ trường để thay thế PostgreSQL |

## 4. Hệ quả

- **Tích cực:** Đúng kế hoạch; chỉ có một nguồn sự thật nên dữ liệu không bị lệch; giải pháp rẻ, dễ làm và dễ kiểm chứng.
- **Tiêu cực / rủi ro chấp nhận:** Mỗi lần chuyển trạng thái phát sinh thêm một lần ghi Redis; hash có thể thiếu hoặc cũ khi Redis lỗi (chấp nhận được vì không có thành phần nào đọc); trong Tuần 7 code ghi chưa có người dùng.
- **Cần làm thêm / theo dõi:** Xem lại ở Tuần 12 (đa instance): quyết định có đọc hash để debug hoặc hỗ trợ định tuyến hay không; nếu có thì lập ADR mới.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** `TaskRuntimeState` trong Redis là write-only ở giai đoạn này, chỉ phục vụ debug và chuẩn bị đa instance; mọi truy vấn trạng thái Task đọc từ PostgreSQL.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-01 | Làm như mô tả (A43, A44 giữ nguyên) |
| Task A-02 | Làm như mô tả; việc ghi state đi qua `TransitionAsync` như A-01 quy định |
| Task B-05 | Giữ nguyên tests 6–10 (kiểm tra hash, TTL và việc Redis lỗi không đổi kết quả); không test nào yêu cầu service đọc hash |
| Spec / Decision Log / diagram | Spec 6.4 ghi rõ "write-only ở giai đoạn này"; Spec 7.3 sửa thành "đọc PostgreSQL" (bỏ "đọc Redis, fallback PostgreSQL"); Decision Log thêm A43, A44 (nguồn ADR-W7-01); `Protocol-Sequence-Diagrams` mục 5 và D13 sửa tương tự; D7 chú thích write-only |
| Contract (`01-shared-contract.md`) | A43, A44: bỏ thẻ `[ADR-W7-01]`, nội dung giữ nguyên |

## 6. Cách kiểm chứng

- **Unit test (A-01):** state được ghi sau commit; compare-and-set thua thì không ghi; store ném lỗi không làm đổi `Result` của `CreateAsync`, `DispatchAsync`, `ReceiveResultAsync`.
- **Integration test:** IT-W7-06 … IT-W7-10.
- **Thủ công:** chạy A → B (B `slow`), rồi `redis-cli HGETALL aers:task:<id>` thấy đủ sáu trường với `status=working`; `redis-cli TTL aers:task:<id>` xấp xỉ `deadline − now + 60`. Sau khi `completed`, TTL không quá 60 giây. `GET /tasks/<id>` vẫn trả đủ `output` (chứng minh dữ liệu đọc từ PostgreSQL). Dừng Redis rồi chạy lại A → B: Task vẫn `completed`, history đầy đủ.
- **Rà soát code:** `ITaskRuntimeStateStore.GetAsync` không có lời gọi nào trong code production ngoài test.

## 7. Ghi chú

Điều kiện xem lại: bắt đầu Tuần 12 (đa instance) hoặc khi có nhu cầu đọc nhanh trạng thái Task. Tham chiếu: A33, Spec 6.4 và 7.3.

---

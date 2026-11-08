# ADR-W9-02: Kiểm tra output theo `outputSchema`: chỉ ghi log cảnh báo, Task vẫn `completed`

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W9-02 |
| **Status** | Proposed |
| **Ngày** | 2026-11-07 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-06, B-07 |
| **Thay thế / liên quan** | Không |

## 1. Bối cảnh

Mỗi capability có `outputSchema`, nhưng hiện AERS không dùng schema này khi nhận `task.result`. Kiểm tra schema là việc của hạ tầng (không cần hiểu nghiệp vụ), nhưng sẽ thay đổi hành vi đối với Agent. Cần quyết định AERS có kiểm tra output hay không, và nếu có thì xử lý vi phạm như thế nào.

## 2. Quyết định

Chọn phương án 2: kiểm tra `output` theo `outputSchema`, chỉ ghi log cảnh báo khi vi phạm, Task vẫn `completed`.

- Chỉ áp dụng cho `task.result` có trạng thái `completed`. Kết quả `failed` không kiểm tra.
- Trong `TaskService.ReceiveResultAsync`, sau các kiểm tra payload hiện có và trước khi chuyển trạng thái, tải schema output của capability thuộc Agent đích (`GetWithCapabilitiesAsync(task.TargetAgentId)`, thêm một truy vấn) rồi gọi `IInputSchemaValidator.Validate(outputSchema, output)`.
- Khi có vi phạm: ghi một warning gồm `taskId`, tên capability và số vi phạm (không ghi `output`); Task vẫn chuyển sang `completed` và kết quả được chuyển tiếp như bình thường.
- Không tìm thấy Agent hoặc capability, hoặc bản thân bước kiểm tra ném exception: bỏ qua kiểm tra, ghi log và vẫn hoàn tất Task (kiểm tra này không bao giờ làm đổi kết quả).
- Không làm: mã lỗi mới, cấu hình mới, audit event mới, hay đổi bất kỳ ack hoặc kết quả nào gửi cho Agent.

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Không kiểm tra output | Không tốn truy vấn; không đổi hành vi | `outputSchema` chỉ còn là tài liệu; Agent sai hợp đồng không bị phát hiện | Không chọn; vì mất khả năng phát hiện Agent vi phạm hợp đồng trong khi chi phí kiểm tra thấp |
| 2 | Kiểm tra, chỉ ghi log cảnh báo, Task vẫn `completed` | Phát hiện Agent sai hợp đồng mà không phá luồng | Thêm một truy vấn capability cho mỗi kết quả; lỗi chỉ xuất hiện trong log | Chọn; vì output của Agent dùng AI khó luôn khớp contract, và fail Task chỉ vì lệch schema gây trải nghiệm xấu và làm phức tạp hệ thống |
| 3 | Kiểm tra và fail Task với mã lỗi mới `OUTPUT_INVALID` | Cưỡng chế hợp đồng: caller luôn nhận output đúng schema | Dễ fail Task hợp lệ về nghiệp vụ; thêm mã lỗi và cách xử lý mới | Không chọn; vì rủi ro làm hỏng luồng lớn hơn lợi ích |

## 4. Hệ quả

- **Tích cực:** Phát hiện được Agent trả output sai hợp đồng mà không làm hỏng luồng gọi; hành vi với Agent không đổi.
- **Tiêu cực / rủi ro chấp nhận:** Mỗi kết quả `completed` tốn thêm một truy vấn capability; caller vẫn có thể nhận output sai schema và phải tự kiểm tra; vi phạm chỉ có trong log, không có audit event.
- **Cần làm thêm / theo dõi:** Tuần 14: đo chi phí truy vấn thêm và cân nhắc cache theo cặp (Agent, capability) vì capability không đổi cho đến khi Agent bị deregister. Tuần 13: cân nhắc metric đếm số vi phạm.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** `outputSchema` được AERS kiểm tra ở mức cảnh báo, không cưỡng chế; caller không được giả định output luôn khớp schema.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-06 | Yêu cầu 6 làm như mô tả (kiểm tra, cảnh báo, vẫn `completed`); thêm xử lý bỏ qua khi không tìm thấy capability hoặc khi bước kiểm tra ném lỗi |
| Task B-07 | Yêu cầu 5: ghi trong `agents/jobhunt/README.md` rằng `outputSchema` được AERS kiểm tra ở mức cảnh báo, không cưỡng chế. Contract test (A62) vẫn meta-validate cả `outputSchema`; không cần ví dụ output |
| Spec / Decision Log / diagram | Spec: ghi ở phần Task Result (mục 5.2) hoặc phần Capability rằng output được validate theo `outputSchema` ở mức cảnh báo. Decision Log: thêm quyết định này. Diagram: không đổi |
| Contract (`01-shared-contract.md`) | Mục 3.2: bỏ thẻ `[ADR-W9-02]`; mục 3.3 giữ nguyên (`IInputSchemaValidator` được dùng lại, không thêm interface) |

## 6. Cách kiểm chứng

- **Unit test (A-06):** output vi phạm → đúng một warning (có `taskId`, tên capability, số vi phạm), Task `completed`, kết quả chuyển tiếp bình thường; output hợp lệ → không có warning; log không chứa nội dung `output`; validator ném lỗi → Task vẫn `completed`.
- **Thủ công:** cho B trả `output` thiếu một trường `required` của `outputSchema`; `GET /tasks/{id}` trả `status: "completed"`; log có một dòng cảnh báo với `taskId` và tên capability; `grep` nội dung `output` trong log → 0 kết quả.

## 7. Ghi chú

Điều kiện xem lại: caller thực tế cần output được bảo đảm theo schema (khi đó cân nhắc phương án 3 hoặc chế độ cấu hình theo từng capability), hoặc số vi phạm trong log cho thấy Agent thường xuyên sai hợp đồng.

---

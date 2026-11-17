# ADR-W8-02: Tin cậy `parentTaskId` do caller khai báo: chấp nhận hạn chế và gắn cờ audit `orphanSuspect`

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W8-02 |
| **Status** | Proposed |
| **Ngày** | 2026-11-07 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-06, B-04, B-06 |
| **Thay thế / liên quan** | Không (liên quan ADR-W8-01) |

## 1. Bối cảnh

`ancestorChain` và `depth` được tính từ `parentTaskId` do caller tự gửi. Một Agent cố ý (hoặc bị lỗi) có thể bỏ `parentTaskId` để tạo Task gốc mới: khi đó `depth = 0` và chuỗi chỉ có `[caller]`, nên vòng gọi và độ sâu bị che.

Agent trung thực luôn dùng `parentTaskId`, vì vậy hạn chế này chỉ ảnh hưởng Agent không hợp tác. Charter đặt mục tiêu "chặn vòng gọi/độ sâu" ở mức hạ tầng cho Agent hợp tác, nhưng yêu cầu bảo mật có thể cao hơn. Cần quyết định chặn hẳn khả năng này hay chấp nhận và giảm thiểu.

## 2. Quyết định

Chọn phương án 1: chấp nhận như hạn chế đã biết, chặn gián tiếp bằng rate limit, và thêm cờ audit `orphanSuspect`.

- Khi `task.create` không có `parentTaskId` và caller đang là target của ít nhất một Task `working` (`target_agent_id` là caller, `status = working`), audit `task.created` có thêm `details.orphanSuspect = true` (cùng với `targetAgentId` và `capability`). Trường này chỉ xuất hiện khi giá trị là `true`.
- Không từ chối request, không đổi ack, không thêm mã lỗi hay cấu hình.
- Chi phí: một truy vấn kiểm tra tồn tại (`AnyAsync`, dùng index `target_agent_id`) và chỉ chạy khi `parentTaskId` là null.
- Không làm: từ chối Task gốc đáng ngờ, tự suy ra `parentTaskId` ở phía server.

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Chấp nhận như hạn chế đã biết, chặn gián tiếp bằng rate limit; thêm cờ audit `orphanSuspect` | Đơn giản; không đổi hành vi với Agent; vẫn có dữ liệu để truy vết | Không ngăn được Agent cố ý bỏ `parentTaskId`; depth và cycle chỉ bảo vệ Agent hợp tác | Chọn; vì Charter nhắm tới Agent hợp tác, thiệt hại bị giới hạn bởi rate limit và deadline của từng Task, và AERS không suy đoán ý định của Agent (Dumb Relay) |
| 2 | Từ chối Task gốc khi caller đang là target của Task `working` (mã lỗi mới) | Chặn được cách bỏ `parentTaskId` thông thường | Sai với Agent hợp lệ vừa xử lý Task vừa khởi tạo việc độc lập; cần mã lỗi và test mới; AERS bắt đầu suy diễn ý định | Không chọn; vì có false positive và lệch nguyên tắc Dumb Relay |
| 3 | Server bỏ qua `parentTaskId` do caller gửi và tự suy ra từ Task `working` của caller | Caller không thể che chuỗi | Mơ hồ khi caller có nhiều Task `working`; phá Task gốc hợp lệ; đổi hợp đồng protocol | Không chọn; vì không xác định được cha duy nhất và thay đổi protocol quá lớn |

## 4. Hệ quả

- **Tích cực:** Không đổi protocol và hành vi hiện có; có dấu vết audit để điều tra; một vòng lặp tạo Task gốc liên tục vẫn bị giới hạn bởi rate limit (ADR-W8-01) và deadline của từng Task.
- **Tiêu cực / rủi ro chấp nhận:** Agent không hợp tác vẫn che được độ sâu và vòng gọi; cờ `orphanSuspect` có thể dương tính giả (Agent hợp lệ khởi tạo việc độc lập khi đang xử lý Task) và chỉ mang tính gợi ý, không phải kết luận.
- **Cần làm thêm / theo dõi:** Tuần 13: thống kê số audit `orphanSuspect` trong phần observability. Xem lại quyết định nếu hệ thống có Agent không tin cậy hoặc hỗ trợ nhiều Tenant.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** Agent có thể bỏ `parentTaskId` để tạo Task gốc mới và khi đó depth và cycle protection không áp dụng cho chuỗi đó; AERS chỉ bảo vệ chuỗi gọi của Agent hợp tác.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-06 | Yêu cầu 4 làm như mô tả: thêm `ITaskRepository.CountWorkingAsTargetAsync(Guid agentId)` (kiểm tra tồn tại, dùng index `target_agent_id`) và cờ `orphanSuspect` trong audit `task.created`; không từ chối request |
| Task B-04 | Scenario `orphan-root`: kỳ vọng Task được chấp nhận và audit `task.created` có `orphanSuspect = true` (kiểm tra bằng SQL) |
| Task B-06 | Test 30 làm như mô tả: có cờ khi caller giữ Task `working` mà không gửi `parentTaskId`; Task con có `parentTaskId` hoặc Task gốc của Agent không giữ Task `working` thì không có cờ |
| Spec / Decision Log / diagram | Spec 6.5: `details` của `task.created` thêm `orphanSuspect` (tùy chọn); mục Task Ancestry (mục 4) ghi hạn chế đã biết. Decision Log: thêm quyết định này. Diagram: không đổi |
| Contract (`01-shared-contract.md`) | Mục 2.2: bỏ thẻ `[ADR-W8-02]`; README mục 10 giữ dòng giới hạn "Agent có thể bỏ `parentTaskId`" |

## 6. Cách kiểm chứng

- **Unit test (A-06):** cờ được đặt khi caller giữ Task `working` và không gửi `parentTaskId`; không đặt trong các trường hợp còn lại; request không bị từ chối.
- **Integration test:** IT-W8-30.
- **Thủ công:** B nhận một Task (đang `working`), dùng scenario `orphan-root` để B tạo Task mới không có `parentTaskId`, sau đó chạy `SELECT details FROM audit_events WHERE event_type = 'task.created' ORDER BY id DESC LIMIT 1;` → `details` chứa `"orphanSuspect": true`. Tạo Task con kèm `parentTaskId` thì audit không có trường này.

## 7. Ghi chú

Điều kiện xem lại: có yêu cầu bảo mật cao hơn Charter (Agent không tin cậy, đa Tenant), hoặc số cờ `orphanSuspect` cho thấy lạm dụng thực tế. Khi đó cân nhắc phương án 2 hoặc một cơ chế ràng buộc cha–con ở protocol.

---

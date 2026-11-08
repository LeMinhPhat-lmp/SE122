# ADR-W7-03: Ngữ nghĩa khi Target Agent ngắt kết nối: ân hạn rồi mới fail Task

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W7-03 |
| **Status** | Proposed |
| **Ngày** | 2026-11-06 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-04, B-04, B-05 |
| **Thay thế / liên quan** | Không (liên quan ADR-W7-02 và ADR-W7-04) |

## 1. Bối cảnh

Task `working` đã gửi `task.invoke` cho B. Nếu B rớt kết nối (mạng chập chờn hoặc crash) thì chưa rõ AERS phải làm gì. B có thể kết nối lại và vẫn gửi `task.result`; kết quả này vẫn được chấp nhận vì AERS kiểm tra theo agent id của sender, không theo connection.

Contract (A45) đề xuất không làm gì khi ngắt kết nối: nếu B crash thật thì caller phải chờ hết deadline (mặc định 300 giây).

## 2. Quyết định

Chọn phương án 3: ân hạn. Ghi nhận thời điểm B ngắt kết nối; nếu sau `TargetDisconnectGraceSeconds` B chưa nối lại thì worker chuyển các Task `working` của B sang `failed`.

- **Ghi dấu khi ngắt kết nối:** khi `OnDisconnectedAsync` của Agent B unbind (compare-and-delete) thành công, ghi dấu vào Redis ZSET `{KeyPrefix}disconnected` (member là `agentId`, score là unix milliseconds). Nếu kết nối bị ngắt đã không còn là kết nối hiện tại trong registry (B đã nối lại bằng kết nối mới) thì không ghi dấu.
- **Xóa dấu khi nối lại:** khi B bind kết nối thành công trong `OnConnectedAsync`, xóa dấu của B (`ZREM`).
- **Xử lý hết ân hạn:** thêm bước thứ hai vào `RunOnceAsync` của `TaskTimeoutWorker` (A-02), chạy sau bước hết hạn Task, dùng chung chu kỳ `TimeoutScanIntervalSeconds`. Worker lấy tối đa `TimeoutBatchSize` Agent có dấu cũ hơn `TargetDisconnectGraceSeconds`. Với mỗi Agent: xác nhận vẫn không có kết nối (`IConnectionRegistry.ResolveAsync` trả null); lấy tối đa `TimeoutBatchSize` Task `working` có `target_agent_id` là Agent đó (dùng index sẵn có); chuyển từng Task từ `working` sang `failed` bằng compare-and-set qua `TransitionAsync` (history và audit `task.status_changed` trong một transaction) với lỗi `TaskError("TARGET_UNAVAILABLE", "The target agent disconnected and did not reconnect within the grace period.", null, true)`. Sau commit, worker push `task.result` cho caller qua `ITaskNotifier`. Khi Agent không còn Task `working` nào thì xóa dấu.
- **Race:** nếu B nối lại và trả kết quả trước thì compare-and-set của worker thua và worker bỏ qua; nếu worker thắng thì `task.result` đến muộn của B nhận `TASK_STATE_CONFLICT` (A50).
- **Cấu hình mới:** `Aers:Tasks:TargetDisconnectGraceSeconds` = 30, phải ≥ 1, validate lúc khởi động.
- **Lưới an toàn:** Redis lỗi khi ghi hoặc đọc dấu chỉ ghi warning; Task vẫn kết thúc bằng `TASK_TIMEOUT` khi tới deadline. Restart AERS làm mất dấu (registry bị purge khi khởi động) nên Task cũng chờ `TASK_TIMEOUT`.
- **Không làm:** fail ngay khi ngắt kết nối; gửi lại `task.invoke` sau khi B nối lại; xử lý Task `submitted`; để caller ngắt kết nối ảnh hưởng đến Task (giữ A45 cho phần caller).

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Chờ timeout (không làm gì khi ngắt kết nối) | Đơn giản; chịu được rớt mạng ngắn; không thêm cơ chế | Nếu B crash thật, caller chờ đến hết deadline (mặc định 300 giây) | Không chọn; vì không phân biệt được B crash thật với ngắt kết nối do mạng nên caller luôn phải chờ tối đa |
| 2 | Fail ngay mọi Task `working` của B bằng `TARGET_UNAVAILABLE` khi B ngắt kết nối | Caller biết lỗi sớm | Rớt mạng thoáng qua làm hỏng Task đang chạy tốt; cần truy vấn Task theo target mỗi lần ngắt kết nối | Không chọn; vì cũng không phân biệt được crash với rớt mạng, và Task đã fail thì B nối lại cũng không thể hoàn thành nữa |
| 3 | Ân hạn: ghi dấu thời điểm ngắt kết nối, worker fail Task nếu B chưa nối lại sau N giây (mặc định 30) | Cân bằng giữa phát hiện sớm và chịu được rớt mạng ngắn | Thêm state (ZSET Redis) và một bước trong worker | Chọn; vì phân biệt được B crash với rớt mạng thoáng qua; chi phí vừa phải (một ZSET và một bước trong worker có sẵn); Redis chỉ giữ dấu tạm nên mất dấu thì lùi về `TASK_TIMEOUT` |

Dấu ngắt kết nối đặt ở Redis (không phải PostgreSQL) vì đây là dữ liệu tạm, thay đổi thường xuyên và không cần bền vững; trạng thái Task vẫn nằm trong PostgreSQL.

## 4. Hệ quả

- **Tích cực:** Phân biệt được B crash (không nối lại sau ân hạn) với rớt mạng thoáng qua; caller biết lỗi sau khoảng 30 giây thay vì chờ 300 giây; không fail hàng loạt chỉ vì mạng chập chờn.
- **Tiêu cực / rủi ro chấp nhận:**
  - Tăng độ phức tạp: thêm state Redis, thêm bước trong worker, và `OnDisconnectedAsync` thêm một lần ghi Redis.
  - Race khi B nối lại đúng lúc worker fail Task: compare-and-set đảm bảo chỉ một bên thắng, bên thua nhận `TASK_STATE_CONFLICT` (B mất công xử lý).
  - B vẫn sống nhưng mất kết nối lâu hơn 30 giây vẫn bị fail Task.
- **Cần làm thêm / theo dõi:** Xem lại giá trị 30 giây sau load test Tuần 14. Ở Tuần 12 (đa instance), dấu nằm trong Redis dùng chung nên mọi instance đều xử lý được, nhưng cần kiểm tra lại với hai instance thật.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** Task `working` của B bị fail với `TARGET_UNAVAILABLE` nếu B không nối lại trong thời gian ân hạn, kể cả khi B vẫn đang xử lý; `task.invoke` không được gửi lại sau khi B nối lại; restart AERS làm mất dấu nên Task chờ đến `TASK_TIMEOUT`.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-04 | Thay yêu cầu 3 ("không thêm behavior vào `OnDisconnectedAsync`") bằng: ghi dấu khi unbind thành công và xóa dấu khi bind; thêm bước thứ hai vào `RunOnceAsync` của worker; thêm option `TargetDisconnectGraceSeconds`. Bổ sung interface vào contract mục 1.5 mà không đổi chữ ký hiện có (tên gợi ý, chốt khi viết A-04): `IDisconnectTracker` (ghi, xóa, liệt kê dấu đến hạn) và `ITaskService.FailTasksOfDisconnectedTargetAsync`. Yêu cầu 4 (target reconnect vẫn gửi được `task.result`) giữ nguyên |
| Task B-04 | Scenario `target-vanishes`: kỳ vọng `failed/TARGET_UNAVAILABLE` sau ân hạn (chạy server với `Aers__Tasks__TargetDisconnectGraceSeconds=3`) thay vì `TASK_TIMEOUT`. Scenario `target-reconnect`: B nối lại sau 2 giây (ngắn hơn ân hạn), kỳ vọng `completed` |
| Task B-05 | Test 28: thêm kiểm tra dấu của B bị xóa sau khi nối lại. Test 29: trong ân hạn Task vẫn `working`; hết ân hạn Task `failed/TARGET_UNAVAILABLE` (history `working → failed`, một audit `task.status_changed`, caller nhận push); `task.result` đến muộn bị `TASK_STATE_CONFLICT`; chạy với factory đặt `TargetDisconnectGraceSeconds = 2`. Các test khác giữ nguyên |
| Spec / Decision Log / diagram | Spec: viết lại mục ngữ nghĩa disconnect (thay A45); 3.1 lifecycle: `working → failed` có thêm trường hợp target không nối lại; 9.3 thêm option mới; 12.2 ghi chú `TARGET_UNAVAILABLE` còn xuất hiện khi hết ân hạn. Decision Log: thay A45. Diagram: D7 (key `disconnected`), D13 (worker bước hai), D15 |
| Contract (`01-shared-contract.md`) | Viết lại A45 theo ADR này (bỏ thẻ `[ADR-W7-03]`); A51 thêm option mới; mục 1.5 thêm interface; README mục 10 cập nhật giới hạn |

## 6. Cách kiểm chứng

- **Unit test:** chỉ ghi dấu khi unbind thành công (kết nối cũ bị ngắt sau khi đã nối lại không tạo dấu); dấu chưa đủ `TargetDisconnectGraceSeconds` thì không fail Task; Agent đã nối lại thì không fail; compare-and-set thua thì không push; Redis lỗi không làm worker chết.
- **Integration test:** IT-W7-28 và IT-W7-29 theo bảng ở mục 5.
- **Thủ công (target crash):**
  1. Chạy `HubProbe serve` cho B, A gửi Task cho B, rồi tắt tiến trình B.
  2. `redis-cli ZRANGE aers:disconnected 0 -1 WITHSCORES` thấy B.
  3. Sau khoảng `TargetDisconnectGraceSeconds` (dùng giá trị nhỏ khi test), `SELECT status, error FROM tasks WHERE id = '<id>';` cho `failed` với `TARGET_UNAVAILABLE`; ZSET không còn B; A nhận `task.result` thất bại.
- **Thủ công (target nối lại):** tắt B rồi chạy lại trong thời gian ân hạn và trả kết quả → Task `completed`, dấu bị xóa.

## 7. Ghi chú

Giá trị 30 giây là mặc định ban đầu, cần đo lại sau load test. Kết quả thất bại được gửi cho caller bằng cùng cơ chế ack của ADR-W7-02. Deadline của Task con vẫn theo ADR-W7-04, không bị ảnh hưởng bởi ân hạn.

---

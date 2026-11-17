# ADR-W8-03: Thu hồi kết nối hub đang mở khi Agent bị deregister: abort qua Redis pub/sub

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W8-03 |
| **Status** | Proposed |
| **Ngày** | 2026-11-07 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-05, A-07, B-04, B-06 |
| **Thay thế / liên quan** | Không (liên quan ADR-W7-02 và ADR-W8-04) |

## 1. Bối cảnh

Xác thực chỉ xảy ra khi mở kết nối hub. Sau `DELETE /agents/{id}`, key bị thu hồi (request REST mới nhận 401) nhưng kết nối hub đã mở vẫn hoạt động. `task.create` tự từ chối vì `CreateAsync` kiểm tra caller còn active; còn `task.result` và `task.status_event` hiện chỉ so sánh sender với target của Task, không kiểm tra Agent còn active hay không.

Cần quyết định cách vô hiệu hóa kết nối đang mở của Agent đã bị thu hồi, kể cả khi AERS chạy nhiều instance.

## 2. Quyết định

Chọn phương án 3: abort kết nối qua kênh Redis pub/sub mà mọi instance đều lắng nghe, kết hợp kiểm tra active ở mọi message.

- **Phát thông báo thu hồi:** sau khi transaction deregistration commit, `AgentRegistryService` gọi `IConnectionTerminator.TerminateAsync(agentId)`. Chữ ký giữ nguyên; hành vi là publish `agentId` lên kênh Redis `{KeyPrefix}agent:revoked`. Việc này là best-effort: lỗi chỉ ghi warning và kết quả `204` không đổi.
- **Abort ở từng instance:** mỗi instance chạy một hosted service ở tầng API (`ConnectionTerminationSubscriber`) subscribe kênh trên. Khi nhận `agentId`, nếu instance đang giữ kết nối hub của Agent đó thì abort kết nối; nếu không thì bỏ qua. Hub quản lý một dictionary kết nối đang sống theo `agentId` (thêm ở `OnConnectedAsync`, dọn ở `OnDisconnectedAsync`) vì mỗi Agent chỉ có một kết nối. Message sai định dạng bị bỏ qua và ghi warning (không kèm payload).
- **Dọn mapping:** sau khi abort, `OnDisconnectedAsync` unbind compare-and-delete như thường lệ nên mapping `conn:{agentId}` bị xóa.
- **Kiểm tra active ở mọi message:** `task.result`, `task.status_event` (và `task.create`, `task.ack` theo ADR-W7-02) coi sender không còn active giống như `CreateAsync` đối với caller không active: `AUTHENTICATION_FAILED`, `Unauthorized`. Đây là lớp phòng thủ khi message pub/sub bị mất.
- **Không thêm package:** dùng `ISubscriber` của StackExchange.Redis đã có.
- **Không làm:** SignalR Redis backplane và định tuyến message giữa các instance (Tuần 12); lưu hay retry thông điệp thu hồi; abort khi chỉ key bị thu hồi mà Agent chưa deregister (MVP không có rotate key).

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Abort kết nối trên cùng instance và kiểm tra active cho `task.result`/`task.status_event` (mặc định của A-05) | Đơn giản, không thêm hạ tầng | Chỉ có hiệu lực trên instance đang giữ kết nối; sai khi chạy nhiều instance ở Tuần 12 | Không chọn; vì phải làm lại khi chuyển sang đa instance |
| 2 | Chỉ kiểm tra active ở mọi message, không abort kết nối | Ít code; đúng với mọi instance | Kết nối của Agent đã thu hồi vẫn mở, tiếp tục nhận push và chiếm tài nguyên | Không chọn; vì vẫn để lại kết nối sống của Agent đã bị thu hồi |
| 3 | Abort qua kênh Redis pub/sub mà mọi instance đều nghe | Đúng cả khi đa instance; kết nối bị đóng chủ động | Thêm một thành phần hạ tầng (kênh và hosted service), sớm hơn kế hoạch Tuần 12 | Chọn; vì không phải thiết kế lại khi đa instance, chi phí thấp (Redis sẵn có, không thêm package), và có kiểm tra active làm lớp dự phòng |

## 4. Hệ quả

- **Tích cực:** Thu hồi có hiệu lực trên mọi instance mà không phải đổi thiết kế ở Tuần 12; kết nối của Agent bị thu hồi được đóng trong khoảng 1 giây; có hai lớp bảo vệ (abort và kiểm tra active).
- **Tiêu cực / rủi ro chấp nhận:** Thêm một kênh pub/sub và một hosted service sớm hơn kế hoạch; Redis pub/sub không đảm bảo giao message (instance mất kết nối Redis trong lúc publish sẽ không nhận được); mỗi instance giữ một subscription.
- **Cần làm thêm / theo dõi:** Tuần 12: kiểm thử với hai instance thật qua load balancer và xem xét gộp với backplane nếu cần.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** Nếu message thu hồi bị mất (Redis lỗi đúng lúc deregister), kết nối của Agent đã thu hồi có thể còn mở cho đến khi tự ngắt; trong thời gian đó Agent không gửi được message có hiệu lực vì mọi message đều bị kiểm tra active, nhưng vẫn có thể nhận push gửi tới mapping còn tồn tại.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-05 | Yêu cầu 1 đổi: `ConnectionTerminator` publish lên kênh thay vì abort trực tiếp; thêm `ConnectionTerminationSubscriber`; hub quản lý dictionary kết nối sống theo `agentId` và dọn khi disconnect. Yêu cầu 2 và 3 làm như mô tả. Unit test: publish đúng một lần sau commit; terminator lỗi không đổi `204`; subscriber abort đúng kết nối và bỏ qua message lạ |
| Task A-07 | Ma trận lỗi: publish lỗi → warning, deregistration vẫn thành công |
| Task B-04 | Scenario `revoked-key`: kết nối đang mở bị đóng sau khi deregister, và kết nối mới bằng key cũ nhận 401 |
| Task B-06 | Test 23: kết nối đóng trong 5 giây và mapping `conn:{agentId}` bị xóa; thêm biến thể hai host dùng chung Redis (kết nối ở host 1, `DELETE` gọi qua host 2). Test 24: giữ nguyên. Test 25: biến thể tắt terminator vẫn bị từ chối bằng kiểm tra active |
| Spec / Decision Log / diagram | Spec mục 8: thêm đoạn "thu hồi credential và kết nối đang mở", ghi tên kênh Redis. Decision Log: thêm quyết định này và đóng phần kênh của OP-2. Diagram: Sequence Deregistration thêm publish và abort; D1 thêm use case terminate connection; D7 thêm kênh; D9 |
| Contract (`01-shared-contract.md`) | Mục 2.2: bỏ thẻ `[ADR-W8-03]`; mục 2.3: sửa mô tả `TerminateAsync` từ "abort trên instance này" thành "publish thu hồi; mọi instance abort kết nối cục bộ của Agent"; README mục 10: bỏ giới hạn "chỉ có hiệu lực trên instance giữ kết nối" |

## 6. Cách kiểm chứng

- **Unit test:** theo danh sách ở A-05 (terminator, hook deregister, `ReceiveResultAsync` và `ReceiveStatusEventAsync` với sender đã deregister trả `AUTHENTICATION_FAILED`).
- **Integration test:** IT-W8-23, IT-W8-24, IT-W8-25.
- **Thủ công (một instance):**
  1. Chạy `HubProbe serve` cho B; mở terminal khác chạy `redis-cli SUBSCRIBE aers:agent:revoked`.
  2. `DELETE /agents/{B}` → terminal subscribe nhận được `agentId`; kết nối của HubProbe đóng trong 1 giây và không dùng lại được.
  3. `redis-cli HGETALL aers:conn:<B>` trả về rỗng; `curl` với key cũ trả `401`.
- **Thủ công (hai instance):** chạy hai instance ở hai cổng; B kết nối vào instance 1, gọi `DELETE` qua instance 2 → kết nối của B ở instance 1 bị đóng.

## 7. Ghi chú

Redis pub/sub không lưu message nên phải dựa vào kiểm tra active ở mọi message để phòng trường hợp mất thông điệp. Điều kiện xem lại: khi triển khai SignalR Redis backplane ở Tuần 12 (xem xét dùng chung cơ chế), hoặc khi có cơ chế xoay vòng key (OP-4) cần abort theo từng key.

---

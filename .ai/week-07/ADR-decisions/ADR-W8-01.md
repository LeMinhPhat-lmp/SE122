# ADR-W8-01: Rate limiting: kiểm tra sớm trong pipeline, fail-closed khi Redis lỗi, chưa giới hạn REST

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W8-01 |
| **Status** | Proposed |
| **Ngày** | 2026-11-07 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | B-01, A-06, B-04, B-06 |
| **Thay thế / liên quan** | Không (liên quan ADR-W8-02) |

## 1. Bối cảnh

Đã chốt (D-17, A56): fixed window theo `callerAgentId`, 100 `task.create` mỗi 60 giây, Redis `INCR` + `EXPIRE`. Chưa chốt ba điểm:

- **(a) Vị trí trong pipeline:** Spec 9.4 đặt rate limit sau khi validate input, nên request sai vẫn tốn CPU và DB (nạp target, parent, validate schema) mà không bị tính quota.
- **(b) Khi Redis lỗi:** limiter ném `StateStoreUnavailableException`, cần quyết định `task.create` bị từ chối hay được cho qua.
- **(c) Phạm vi:** có giới hạn REST (đăng ký, discovery) hay không.

## 2. Quyết định

Chọn (a2) + (b1) + (c1). Thuật toán, key và giá trị mặc định giữ như A56.

- **(a2) Rate limit chạy sớm.** Thứ tự mới trong `TaskService.CreateAsync`: authenticate → shape (400) → caller active → **rate limit** → target exists → parent rules → scope policy (domain, capability, input) → depth → cycle → create. Mỗi `task.create` đã qua shape và caller active tiêu thụ một đơn vị quota, kể cả khi bị từ chối ở bước sau; request bị từ chối ở shape hoặc vì caller không active thì không tính. Request bị từ chối vẫn tăng bộ đếm (B-01).
- **Audit:** mọi từ chối `RATE_LIMITED` vẫn ghi audit `policy.rejected { errorCode }`, không tạo Task, ack `accepted: false`, `taskId: null`, `status: null` (A57, A58). Đây là ngoại lệ so với A53 (vốn chỉ audit từ bước scope policy trở đi) vì rate limit nay đứng trước các bước đó.
- **(b1) Fail-closed.** `IRateLimiter` ném `StateStoreUnavailableException` → `CreateAsync` trả `INTERNAL_ERROR` với `retryable: true`, ghi warning không kèm payload, ghi audit `policy.rejected { errorCode: "INTERNAL_ERROR" }`, không tạo Task. Server không crash.
- **(c1) Không rate limit REST** (đăng ký, discovery) ở giai đoạn này.
- **Cấu hình:** `Aers:Policy:RateLimit:WindowSeconds` = 60, `Aers:Policy:RateLimit:MaxTasksPerWindow` = 100 (đều ≥ 1, validate lúc khởi động).
- **Không làm:** đếm `task.result` hoặc `task.status_event`; rate limit theo IP; giới hạn REST.

## 3. Các phương án đã xem xét

**(a) Vị trí rate limit trong pipeline**

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| a1 | Giữ Spec 9.4: sau validate input | Quota chỉ tính request hợp lệ; khớp Spec | Request sai vẫn tốn DB/CPU mà không bị tính quota | Không chọn; vì không chặn được lưu lượng sai nhưng tốn tài nguyên |
| a2 | Sớm: ngay sau shape và caller active, trước khi nạp target, parent và scope policy | Request flood bị chặn trước các truy vấn tốn kém; chỉ tốn một lần kiểm tra caller | Request lỗi cũng tốn quota; lệch Spec 9.4 và A53 | Chọn; vì bảo vệ DB/CPU là mục đích chính của rate limit |

**(b) Hành vi khi Redis lỗi**

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| b1 | Fail-closed: từ chối bằng `INTERNAL_ERROR`, `retryable: true` | Redis lỗi không làm mất lớp bảo vệ | Redis lỗi thì mọi `task.create` bị từ chối | Chọn; vì khi Redis lỗi thì relay cũng không xác định được kết nối của target, nên cho qua không có lợi thực tế |
| b2 | Fail-open: cho qua và ghi warning | Hệ thống vẫn nhận request | Mất giới hạn đúng lúc hạ tầng đang yếu | Không chọn; vì làm vô hiệu lớp bảo vệ khi cần nhất |

**(c) Phạm vi giới hạn**

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| c1 | Chỉ `task.create`; không giới hạn REST | Đúng phạm vi A56; không thêm cấu hình | Đăng ký và discovery chưa bị giới hạn | Chọn; vì REST đã yêu cầu credential và không nằm trên đường relay; Tuần 13 đã có hạng mục rate limiting |
| c2 | Giới hạn thêm REST bằng ASP.NET Core rate limiting middleware | Bảo vệ toàn bộ API | Thêm cấu hình và test; ngoài phạm vi Tuần 8 | Không chọn; vì chưa cần ở giai đoạn này |

## 4. Hệ quả

- **Tích cực:** DB và CPU được bảo vệ sớm; flood bị chặn bằng một lệnh Redis atomic; Redis lỗi không làm mất bảo vệ; gián tiếp giới hạn Agent cố ý bỏ `parentTaskId` (ADR-W8-02).
- **Tiêu cực / rủi ro chấp nhận:**
  - Request sai cũng tiêu thụ quota.
  - Mỗi lần từ chối `RATE_LIMITED` ghi một dòng audit, nên Agent có key hợp lệ có thể làm phình `audit_events`.
  - Redis lỗi thì mọi `task.create` bị từ chối.
  - REST chưa có giới hạn.
- **Cần làm thêm / theo dõi:** Tuần 13: cân nhắc rate limit cho REST, và chỉ audit lần từ chối đầu tiên trong mỗi cửa sổ nếu `audit_events` tăng nhanh. Tuần 12 và 14: xem lại giá trị 100/60 giây theo kết quả load test.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** Quota tính cả request bị từ chối ở các bước sau; không có rate limit cho REST; fixed window cho phép burst tối đa khoảng 2 lần giới hạn tại ranh giới hai cửa sổ.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task B-01 | Làm như mô tả: limiter ném khi Redis lỗi và không quyết định failure mode; không rate limit REST |
| Task A-06 | Yêu cầu 2: gọi `TryAcquireAsync` ngay sau bước caller active, trước khi nạp target. Yêu cầu 3: mọi request qua shape và caller active đều tiêu thụ quota. Failure mode fail-closed. `RATE_LIMITED` và `INTERNAL_ERROR` do limiter lỗi đều audit `policy.rejected`. Sửa test thứ tự: limiter từ chối thì không nạp target, không `Evaluate`, không depth/cycle; lỗi scope policy thì limiter đã được gọi |
| Task B-04 | Scenario `rate-limit` và `burst`: vẫn kỳ vọng N request đầu được chấp nhận rồi `RATE_LIMITED`; thêm kiểm tra request tới target không tồn tại cũng tiêu thụ quota |
| Task B-06 | Test 10: các ca bị từ chối ở domain, capability, input, depth, cycle đều đã tiêu thụ quota. Test 18: năm request bị `VALIDATION_ERROR` ở input có tiêu thụ quota (create hợp lệ kế tiếp bị `RATE_LIMITED`). Test 19 giữ nguyên (`INTERNAL_ERROR`, `retryable: true`, audit). Test 16: 20 request song song, 5 được chấp nhận, 15 bị `RATE_LIMITED`, mỗi lần từ chối có audit riêng |
| Spec / Decision Log / diagram | Spec 9.4 đổi thứ tự (caller exists → rate limit → target exists → …); mục 10 ghi vị trí và failure mode. Decision Log: bổ sung D-17. Diagram: D14 đổi thứ tự và thêm nhánh Redis lỗi; thêm sequence từ chối do rate limit |
| Contract (`01-shared-contract.md`) | A53: viết lại thứ tự pipeline; A56: ghi (a2), (b1), (c1); mục 2.2: bỏ thẻ `[ADR-W8-01]`; A-06 yêu cầu 3 sửa như trên |

## 6. Cách kiểm chứng

- **Unit test (A-06, B-01):** fake limiter ghi nhận thứ tự gọi; request lỗi shape không gọi limiter; limiter từ chối thì không có truy vấn target; limiter ném lỗi → `INTERNAL_ERROR`, `retryable: true`, có audit, không ném exception ra ngoài; limiter không nuốt `StateStoreUnavailableException`.
- **Integration test:** IT-W8-10, IT-W8-11, IT-W8-12, IT-W8-16, IT-W8-18, IT-W8-19.
- **Thủ công (quota tính cả request sai):** chạy server với limit 5 request mỗi 5 giây; dùng HubProbe gửi 6 `task.create` tới target không tồn tại → 5 request đầu trả `AGENT_NOT_FOUND`, request thứ 6 trả `RATE_LIMITED` kèm `details.resetAt`. `redis-cli GET aers:ratelimit:<agentId>:<windowStart>` ≥ 6 và `TTL` không quá 6 giây.
- **Thủ công (Redis lỗi):** dừng Redis, gửi `task.create` → ack `INTERNAL_ERROR` với `retryable: true`; số dòng bảng `tasks` không đổi; `audit_events` có một dòng `policy.rejected`. Bật lại Redis thì hoạt động bình thường.

## 7. Ghi chú

Nhãn (a1/a2), (b1/b2), (c1/c2) theo thứ tự các phương án trong `open-decisions.md`. Điều kiện xem lại: Tuần 13 (Observability & Security) khi xét rate limit cho REST và giới hạn audit; Tuần 14 khi có số liệu load test.

---

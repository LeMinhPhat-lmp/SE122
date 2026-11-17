# ADR-W8-04: Ghi log lỗi xác thực: cảnh báo giới hạn tốc độ, không ghi `audit_events`; JWT nằm ngoài MVP

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W8-04 |
| **Status** | Proposed |
| **Ngày** | 2026-11-07 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-05, B-06 |
| **Thay thế / liên quan** | Không (liên quan ADR-W8-03) |

## 1. Bối cảnh

Kế hoạch Tuần 13 nhắc "Authentication failure" trong phần quan sát. Lỗi xác thực không có `agentId`, nên khó ghi `audit_events` một cách an toàn: kẻ tấn công không cần credential vẫn có thể làm đầy bảng. Charter ghi "JWT/API Key" nhưng MVP chỉ làm API Key.

Cần quyết định (1) lỗi xác thực được quan sát bằng cách nào và (2) JWT có thuộc MVP hay không.

## 2. Quyết định

Chọn (1) + (J1).

- **(1) Log cảnh báo, không ghi database.** Trong handler xác thực (`AgentAuthenticationHandlerBase`/`ApiKeyAuthenticationHandler`), mỗi lần xác thực thất bại ghi một structured warning gồm: tên scheme, loại lỗi (`missing`, `malformed`, `unknown_or_revoked`), request path, địa chỉ remote (`HttpContext.Connection.RemoteIpAddress`) và cờ hub hay REST.
- **Không bao giờ log** key (thô hoặc băm), header, hay registration key.
- **Không ghi** lỗi xác thực vào `audit_events`.
- **Giới hạn log:** mỗi cặp (địa chỉ remote, loại lỗi) chỉ log tối đa một lần trong 10 giây, bằng bộ nhớ trong của instance (dictionary có giới hạn kích thước tối đa 10.000 mục, mục cũ hơn một phút bị loại). Giá trị này là hằng số trong code, không thêm cấu hình.
- **(J1) JWT nằm ngoài MVP.** Không triển khai JWT; giá trị `JWT` của `authenticationScheme` giữ làm điểm mở rộng và không đổi code.
- **Không làm:** đếm hay metric cho lỗi xác thực (để Tuần 13), lưu lịch sử lỗi xác thực, rate limit REST.

## 3. Các phương án đã xem xét

**(1) Cách quan sát lỗi xác thực**

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Structured warning log có giới hạn tốc độ, không ghi database | Không mở đường làm đầy DB; đủ để điều tra; đơn giản | Log không bền vững và khó truy vấn | Chọn; vì an toàn trước tấn công và đủ cho nhu cầu quan sát ở MVP |
| 2 | Ghi `audit_events` (với `agentId` null) cho mỗi lần thất bại | Truy vấn được, bền vững | Kẻ không có credential có thể làm đầy bảng; không có `agentId` để quy trách nhiệm | Không chọn; vì là vector tấn công làm đầy DB |
| 3 | Không ghi gì, chỉ trả `401` | Đơn giản nhất | Không phát hiện được việc dò key; thiếu hạng mục quan sát của Tuần 13 | Không chọn; vì không quan sát được lỗi xác thực |

**(J) Phạm vi JWT**

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| J1 | JWT nằm ngoài MVP; giữ `JWT` làm điểm mở rộng | Đúng phạm vi và thời gian 15 tuần; khớp Spec 8.1 | Chưa bám hết dòng "JWT/API Key" của Charter | Chọn; vì API Key đã đáp ứng định danh caller của MVP |
| J2 | Triển khai thêm JWT | Bám sát Charter | Cần issuer, signing key và quy trình cấp/hết hạn token; ngoài phạm vi | Không chọn; vì tốn công mà không bổ sung giá trị cho các tiêu chí nghiệm thu |

## 4. Hệ quả

- **Tích cực:** Lỗi xác thực quan sát được mà không lộ secret và không mở đường làm đầy DB; log không bị flood nhờ giới hạn tốc độ; phạm vi xác thực của MVP rõ ràng.
- **Tiêu cực / rủi ro chấp nhận:** Không có bản ghi bền vững hay truy vấn được về lỗi xác thực; không biết Agent nào đang bị dò key vì không có `agentId`; bộ giới hạn log nằm trong bộ nhớ của từng instance.
- **Cần làm thêm / theo dõi:**
  - Tuần 12: khi có load balancer, địa chỉ remote sẽ là địa chỉ proxy nên cần cấu hình forwarded headers với proxy tin cậy, nếu không giới hạn log theo địa chỉ sẽ gộp mọi client làm một.
  - Tuần 13: thêm metric đếm lỗi xác thực theo scheme và loại lỗi.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** JWT nằm ngoài MVP, xác thực chỉ dùng API Key; lỗi xác thực chỉ có trong log, không có trong database.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-05 | Yêu cầu 4 và 5 làm như mô tả; thêm câu "JWT is out of the MVP" vào `docs/week-07-implementation.md` |
| Task B-06 | Test 27 làm như mô tả: log cảnh báo có scheme, path, địa chỉ remote và không có nội dung key; không có dòng nào được thêm vào `audit_events`. Test 20 giữ nguyên |
| Spec / Decision Log / diagram | Spec 8.1 giữ câu "JWT chưa triển khai"; Spec 8.5 bổ sung quy tắc log lỗi xác thực. Decision Log: bổ sung D-04 (log lỗi xác thực, JWT ngoài MVP). Diagram: không đổi |
| Contract (`01-shared-contract.md`) | Mục 2.2: bỏ thẻ `[ADR-W8-04]`; A58 giữ nguyên (lỗi xác thực không ghi database). Charter không sửa (nội dung ở mức charter, Spec 8.1 xác định MVP chỉ API Key, thống nhất với sync report T4-00) |

## 6. Cách kiểm chứng

- **Unit test (A-05):** lần lỗi thứ hai giống hệt trong 10 giây không được log; địa chỉ hoặc loại lỗi khác thì được log; dictionary không vượt giới hạn; log bắt được không chứa nội dung key.
- **Integration test:** IT-W8-20 và IT-W8-27.
- **Thủ công:**
  1. Gọi `curl` ba lần trong 10 giây với key sai → chỉ có một dòng warning trong log.
  2. `SELECT count(*) FROM audit_events;` trước và sau không đổi.
  3. `grep` nội dung key đã dùng trong log → 0 kết quả.

## 7. Ghi chú

Điều kiện xem lại: Tuần 12 (load balancer, forwarded headers) và Tuần 13 (metric, tracing). Nếu cần bản ghi bền vững về lỗi xác thực thì dùng log sink hoặc metric, không dùng `audit_events`.

---

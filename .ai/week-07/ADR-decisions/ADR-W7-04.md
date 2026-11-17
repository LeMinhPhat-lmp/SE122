# ADR-W7-04: Deadline của Task con: chặn không vượt quá deadline của Task cha

| Trường | Giá trị |
|---|---|
| **ID** | ADR-W7-04 |
| **Status** | Proposed |
| **Ngày** | 2026-11-06 |
| **Người quyết định** | Lê Minh Phát (A) |
| **Task bị ảnh hưởng** | A-02, B-04, B-05 |
| **Thay thế / liên quan** | Không (liên quan ADR-W7-03) |

## 1. Bối cảnh

Mỗi Task có `deadlineAt = createdAt + 300 giây`. Task con được tạo muộn có thể hết hạn sau Task cha: cha thất bại do timeout nhưng con vẫn chạy "mồ côi" và tốn tài nguyên.

Cần quyết định AERS xử lý quan hệ deadline giữa Task cha và Task con như thế nào.

## 2. Quyết định

Chọn phương án 1: chỉ chặn deadline của Task con, không hủy dây chuyền.

- Với Task tạo kèm `parentTaskId`: `deadlineAt(con) = min(now + DefaultTaskTimeoutSeconds, deadlineAt(cha))`. Task gốc giữ `now + DefaultTaskTimeoutSeconds`.
- Nếu Task cha đã quá deadline lúc tạo con thì Task con vẫn được tạo với deadline đó và sẽ bị worker hết hạn ở chu kỳ kế tiếp; không thêm mã lỗi mới.
- Khi Task cha hết hạn hoặc thất bại, AERS không hủy các Task con đang chạy; mỗi Task con tự hết hạn bằng `TASK_TIMEOUT`, không muộn hơn deadline của cha.
- Không làm: truy vấn cây theo `parent_task_id`, fail dây chuyền, hoặc push thêm cho các caller của Task con.

## 3. Các phương án đã xem xét

| Phương án | Mô tả | Ưu | Nhược | Kết luận |
|---|---|---|---|---|
| 1 | Chỉ chặn deadline: `deadline(con) = min(now + timeout, deadline(cha))`; con tự hết hạn không muộn hơn cha | Rẻ (một phép `min`), không cần cơ chế hủy | Con vẫn chạy đến deadline của cha dù cha đã fail trước đó | Chọn; vì đơn giản, kiểm soát được và đủ để con không sống lâu hơn deadline của cha |
| 2 | Chặn deadline và fail dây chuyền: khi cha hết hạn hoặc fail, AERS fail luôn các con đang chạy (truy vấn theo `parent_task_id`) | Dọn sạch ngay | Cần truy vấn cây đệ quy và push cho nhiều caller | Không chọn; vì chưa có giải pháp rõ cho cây gọi rất sâu hoặc rất rộng khi hệ thống mở rộng; có thể xét lại sau |
| 3 | Không làm gì (mỗi Task độc lập) | Đơn giản nhất | Con có thể sống lâu hơn cha, kết quả của con trở nên vô nghĩa | Không chọn; vì kết quả của con không còn ai chờ nhưng vẫn chiếm tài nguyên |

## 4. Hệ quả

- **Tích cực:** Task con không bao giờ sống lâu hơn deadline của Task cha, nên không tồn tại Task "mồ côi" vô hạn; chi phí thực hiện rất thấp.
- **Tiêu cực / rủi ro chấp nhận:** Chặn deadline không xử lý trường hợp Task cha thất bại sớm (ví dụ B trả `failed`): khi đó Task con vẫn chạy đến deadline của nó và tiêu thụ tài nguyên vô nghĩa trong khoảng thời gian đó.
- **Cần làm thêm / theo dõi:** Nếu load test (Tuần 14) cho thấy Task con mồ côi gây tải đáng kể thì xem lại phương án 2.
- **Hạn chế đã biết đưa vào tài liệu/báo cáo:** Hết hạn hoặc thất bại của Task cha không hủy Task con đang chạy; Task con chỉ bảo đảm không hết hạn muộn hơn deadline của cha.

## 5. Ảnh hưởng đến công việc và tài liệu

| Hạng mục | Thay đổi |
|---|---|
| Task A-02 | Làm như mô tả (yêu cầu 4: công thức `min` ở `CreateAsync`; giữ câu "hết hạn Task cha không hủy Task con") |
| Task B-04 | Scenario `child-deadline` giữ nguyên: Task con kết thúc `failed/TASK_TIMEOUT` không muộn hơn Task cha |
| Task B-05 | Giữ nguyên tests 17 và 18; không thêm test fail dây chuyền |
| Spec / Decision Log / diagram | Spec mục 11 (timeout): thêm quy tắc deadline của Task con và câu "không hủy dây chuyền". Decision Log: thêm A47 (nguồn ADR-W7-04). Diagram: D13 và D15 không đổi |
| Contract (`01-shared-contract.md`) | A47: bỏ thẻ `[ADR-W7-04]`, nội dung giữ nguyên |

## 6. Cách kiểm chứng

- **Unit test (A-02):** quy tắc `min` khi Task cha hết hạn sớm hơn `now + timeout`; Task gốc không bị đổi deadline.
- **Integration test:** IT-W7-17 và IT-W7-18.
- **SQL:** `SELECT c.id FROM tasks c JOIN tasks p ON c.parent_task_id = p.id WHERE c.deadline_at > p.deadline_at;` phải trả về 0 dòng.
- **Thủ công:** chạy scenario `child-deadline` (server timeout 10 giây), kỳ vọng Task con `failed/TASK_TIMEOUT` với `completed_at` không muộn hơn Task cha.

## 7. Ghi chú

Điều kiện xem lại: Task con mồ côi gây tải đáng kể trong load test, hoặc có yêu cầu hủy dây chuyền. Liên quan: A47 và ADR-W7-03 (Task cha có thể thất bại sớm vì target không nối lại).

---

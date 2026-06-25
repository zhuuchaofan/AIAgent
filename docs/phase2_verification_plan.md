# Phase 2 测试与验证计划 (Verification Plan)

## 一、 安全与多用户隔离验证 (MUST PASS)

必须在每阶段 API 部署前完成跨用户越权与未登录阻断测试。为确保安全性，我们使用具体的 `curl` 命令进行验证。

### 1. 未登录状态阻断 (401 Unauthorized)
不携带 Token 请求任何受保护的 API，均应返回 `401 Unauthorized`。
*   **测试 PUT 接口**：
    ```bash
    curl -i -X PUT http://localhost:5000/api/life/events/test_event_id \
      -H "Content-Type: application/json" \
      -d "{\"title\": \"无凭证修改\"}"
    ```
    *预期响应*：`HTTP/1.1 401 Unauthorized`
*   **测试 DELETE 接口**：
    ```bash
    curl -i -X DELETE http://localhost:5000/api/life/events/test_event_id
    ```
    *预期响应*：`HTTP/1.1 401 Unauthorized`
*   **测试 POST Daily Summary 接口**：
    ```bash
    curl -i -X POST http://localhost:5000/api/daily-summaries/generate \
      -H "Content-Type: application/json" \
      -d "{\"targetDate\": \"2026-06-25\", \"clientTimeZone\": \"Asia/Shanghai\"}"
    ```
    *预期响应*：`HTTP/1.1 401 Unauthorized`

### 2. 跨用户越权访问校验 (404 Not Found 或 403 Forbidden)
假设 UserA 的 Token 为 `TOKEN_A`，UserB 的某个事件 ID 为 `event_b_123`（属于子集合 `users/user_b/life_events/event_b_123`）。
由于路径隔离，UserA 的请求在定位到其名下的该 ID 时（`users/user_a/life_events/event_b_123`）会发现该路径不存在，故直接返回 `404 Not Found`。
*   **UserA 尝试修改 UserB 的事件**：
    ```bash
    curl -i -X PUT http://localhost:5000/api/life/events/event_b_123 \
      -H "Authorization: Bearer TOKEN_A" \
      -H "Content-Type: application/json" \
      -d "{\"title\": \"恶意篡改\"}"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found` (或 `HTTP/1.1 403 Forbidden`)
*   **UserA 尝试删除 UserB 的事件**：
    ```bash
    curl -i -X DELETE http://localhost:5000/api/life/events/event_b_123 \
      -H "Authorization: Bearer TOKEN_A"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found` (或 `HTTP/1.1 403 Forbidden`)
*   **UserA 尝试修改 UserB 的提醒状态**：
    ```bash
    curl -i -X PATCH http://localhost:5000/api/reminders/reminder_b_999 \
      -H "Authorization: Bearer TOKEN_A" \
      -H "Content-Type: application/json" \
      -d "{\"status\": \"completed\"}"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found` (或 `HTTP/1.1 403 Forbidden`)

### 3. 数据迁移的安全防范校验 (403 Forbidden)
数据迁移接口 `/api/migration/run-phase2a0` 绝不允许越权或无凭证执行。
*   **测试无迁移密钥调用**（仅携带普通用户 Token）：
    ```bash
    curl -i -X POST http://localhost:5000/api/migration/run-phase2a0 \
      -H "Authorization: Bearer TOKEN_A"
    ```
    *预期响应*：`HTTP/1.1 403 Forbidden`
*   **测试携带错误迁移密钥调用**：
    ```bash
    curl -i -X POST http://localhost:5000/api/migration/run-phase2a0 \
      -H "Authorization: Bearer TOKEN_A" \
      -H "X-Migration-Secret: WRONG_SECRET"
    ```
    *预期响应*：`HTTP/1.1 403 Forbidden`

---

## 二、 存量数据迁移验证 (Phase 2A-0)

### 1. Firestore Console 数据抽查
1. 打开 Firebase Console 进 Firestore 数据库。
2. 选取至少 3 个在 Phase 2A-0 启动前就已经创建的历史 `life_events` 文档。
3. 验证这几个文档均已含有以下字段，且类型正确：
   *   `isDeleted`: `false` (Boolean)
   *   `updatedAt`: 有效的时间戳
   *   `deletedAt`: `null` (Null 类型)

### 2. Timeline API 兼容性验证
使用 UserA 的 Token 请求 Timeline 接口：
```bash
curl -X GET "http://localhost:5000/api/life/events?limit=20" \
  -H "Authorization: Bearer TOKEN_A"
```
*   **验证标准**：响应中必须能够完整包含所有符合条件的历史事件，这证明 `WhereEqualTo("isDeleted", false)` 查询没有因为历史文档缺少字段而被 Firestore 过滤掉。

---

## 三、 Timeline 增强手动验证 (Phase 2A)

### 1. 软删除效果验证
1. 用 Token A 创建一条测试事件，记录其 `id`。
2. 调用软删除接口：
   ```bash
   curl -X DELETE http://localhost:5000/api/life/events/TEST_ID_1 \
     -H "Authorization: Bearer TOKEN_A"
   ```
3. 刷新前端 Timeline，确认 `TEST_ID_1` 不再返回。
4. 去 Firestore Console 检查对应的文档路径 `users/user_a/life_events/TEST_ID_1`：
   *   验证文档物理存活，字段 `isDeleted = true` 且 `deletedAt` 被设为了当前 UTC 时间戳。

### 2. 标签单值筛选验证
1. 使用 Tag `骑行` 过滤：
   ```bash
   curl -X GET "http://localhost:5000/api/life/events?tag=%E9%AA%91%E8%A1%8C" \
     -H "Authorization: Bearer TOKEN_A"
   ```
2. 验证结果列表里的每一条记录其 `tags` 数组中均含有 `"骑行"`。
3. 验证返回的数据绝对不含其他用户的任何事件。

---

## 四、 提醒闭环逻辑验证 (Phase 2B)

### 1. 明确提醒创建与双写
1. 调用 Ingest 接口（请求体使用 `text` 且必须包含 `clientTimeZone`）：
   ```bash
   curl -X POST http://localhost:5000/api/life/ingest \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"text\": \"明天下午3点提醒我给猫剪指甲\", \"clientTimeZone\": \"Asia/Shanghai\"}"
   ```
2. 确认返回的 JSON 中，`reminder` 部分被成功解析且已成功关联创建 Reminder。
3. 检查 Firestore，确认对应的 `users/{uid}/reminders` 集合下产生了对应的物理提醒文档，其中 `dueAt` 不为 null 且 `repeatRule` 为 `"none"`。

### 2. 模糊提醒降级 (不写入 reminders)
1. 调用 Ingest 接口：
   ```bash
   curl -X POST http://localhost:5000/api/life/ingest \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"text\": \"以后记得提醒我买一本新书\", \"clientTimeZone\": \"Asia/Shanghai\"}"
   ```
2. 确认返回结果中 `parseStatus = "missing_due_time"` 且关联 reminder 的 ID 为 null。
3. 检查 Firestore，确认 `users/{uid}/life_events` 产生了此事件文档，但 `users/{uid}/reminders` 集合下**绝对没有**生成物理提醒文档（因为 dueAt 缺失）。

### 3. 状态修改联动与互斥规则验证
1. **测试修改状态与 dueAt 互斥**：对于一个 pending 的提醒，尝试同时修改 `status` 与 `dueAt`：
   ```bash
   curl -i -X PATCH http://localhost:5000/api/reminders/TEST_REM_ID \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"status\": \"completed\", \"dueAt\": \"2026-06-26T09:00:00Z\"}"
   ```
   *预期响应*：`HTTP/1.1 400 Bad Request`（禁止在流转为 completed/cancelled 时修改 dueAt）。
2. **测试非 pending 状态下修改 dueAt**：将提醒修改为 completed 后，再次尝试修改该提醒的 `dueAt`：
   ```bash
   curl -i -X PATCH http://localhost:5000/api/reminders/TEST_REM_ID \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"dueAt\": \"2026-06-27T09:00:00Z\"}"
   ```
   *预期响应*：`HTTP/1.1 400 Bad Request`（在非 pending 状态下禁止修改 dueAt）。

---

## 五、 每日总结逻辑验证 (Phase 2C)

### 1. 自然日时间边界验证
1. 设置客户端时区为 `Asia/Shanghai` (+08:00)，目标日期为 `2026-06-25`。
2. 触发 API：
   ```bash
   curl -X POST http://localhost:5000/api/daily-summaries/generate \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"targetDate\": \"2026-06-25\", \"clientTimeZone\": \"Asia/Shanghai\"}"
   ```
3. 验证后端转换逻辑：
   *   本地自然日范围：`2026-06-25T00:00:00+08:00` 至 `2026-06-26T00:00:00+08:00`。
   *   对应的 UTC 查询范围：`2026-06-24T16:00:00Z` 到 `2026-06-25T16:00:00Z`。
   *   去日志中查验 Firestore Query 转换出的 Timestamp 参数是否精确匹配以上两个 UTC 时间点。
   *   确认只有发生时间在这个区间内的 `LifeEvent` 被送往 LLM 总结。

### 2. 重复生成缓存规则验证
1. 在 2026-06-25 生成过一次总结之后，在不带 `forceRegenerate: true` 的情况下再次调用：
   ```bash
   curl -X POST http://localhost:5000/api/daily-summaries/generate \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"targetDate\": \"2026-06-25\", \"clientTimeZone\": \"Asia/Shanghai\", \"forceRegenerate\": false}"
   ```
2. 验证结果直接秒回，且返回内容与第一次生成的总结完全相同。
3. 携带 `forceRegenerate: true` 调用：
   ```bash
   curl -X POST http://localhost:5000/api/daily-summaries/generate \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"targetDate\": \"2026-06-25\", \"clientTimeZone\": \"Asia/Shanghai\", \"forceRegenerate\": true}"
   ```
4. 验证接口出现明显的网络等待延迟（调用大模型），并返回重新生成的总结（内容发生更新）。

### 3. LLM 异常与日志隔离验证
1. 故意给 LLM 传入错误指令或关闭 LLM API Key 配置，然后调用总结生成接口。
2. 确认 API 没有直接吐出 500 崩溃页面，而是优雅捕获异常并返回降级消息。
3. 查询 `users/{uid}/agent_runs` 集合，确认产生了一条记录。验证在 Firestore 对应文档的 `errorMsg` 中**仅存有脱敏简要错误信息**，而**绝不包含** Exception Stack Trace 堆栈（确保完整堆栈仅输出在后台 Cloud Logging 中）。

---

## 六、 自动化回归测试清单 (Automated Tests)

由于 Phase 2 逻辑日趋复杂，我们需要编写 xUnit 单元测试：
*   **`LifeEventServiceTest.cs`**：验证软删除逻辑只将 `isDeleted` 设为 `true` 并设置 `deletedAt`。验证基于游标的双字段排序（`occurredAt DESC, FieldPath.DocumentId DESC`）及游标翻页的下一页游标生成。
*   **`ReminderServiceTest.cs`**：测试提醒的状态转移限制：仅允许 `pending` -> `completed` / `cancelled`，禁止逆向修改。测试在 completed/cancelled 状态下修改 `dueAt` 或在变更状态为 completed/cancelled 时修改 `dueAt` 将被拒绝。
*   **`AgentRunnerTest.cs`**：测试时区边界转换的单元测试。传入 `"2026-06-25"` 与 `"Asia/Shanghai"`，断言产生的 UTC `start` 与 `end` 必须是 `2026-06-24T16:00:00Z` 和 `2026-06-25T16:00:00Z`。

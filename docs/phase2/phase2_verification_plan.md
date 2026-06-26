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

### 2. 跨用户越权访问校验 (404 Not Found)
假设 UserA 的 Token 为 `TOKEN_A`，UserB 的某个事件 ID 为 `event_b_123`（属于子集合 `users/user_b/life_events/event_b_123`）。
由于路径隔离，UserA 的请求在定位到其名下的该 ID 时（`users/user_a/life_events/event_b_123`）会发现该路径不存在，故直接返回 `404 Not Found`。
*   **UserA 尝试修改 UserB 的事件**：
    ```bash
    curl -i -X PUT http://localhost:5000/api/life/events/event_b_123 \
      -H "Authorization: Bearer TOKEN_A" \
      -H "Content-Type: application/json" \
      -d "{\"title\": \"恶意篡改\"}"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found`
*   **UserA 尝试删除 UserB 的事件**：
    ```bash
    curl -i -X DELETE http://localhost:5000/api/life/events/event_b_123 \
      -H "Authorization: Bearer TOKEN_A"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found`
*   **UserA 尝试修改 UserB 的提醒状态**：
    ```bash
    curl -i -X PATCH http://localhost:5000/api/reminders/reminder_b_999 \
      -H "Authorization: Bearer TOKEN_A" \
      -H "Content-Type: application/json" \
      -d "{\"status\": \"completed\"}"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found`

### 3. 数据迁移的安全防范与生命周期校验
数据迁移接口 `/api/migration/run-phase2a0` 必须满足强密钥和物理禁用安全。
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
*   **测试生命周期禁用逻辑**：
    在配置环境变量 `ENABLE_MIGRATION_API=false` 后重新运行服务，再次发起迁移请求：
    ```bash
    curl -i -X POST http://localhost:5000/api/migration/run-phase2a0 \
      -H "Authorization: Bearer TOKEN_A" \
      -H "X-Migration-Secret: CORRECT_SECRET"
    ```
    *预期响应*：`HTTP/1.1 404 Not Found` 或 `HTTP/1.1 503 Service Unavailable`（证明生产环境中迁移接口已被禁用）。

### 4. PUT 修改已软删除事件拦截校验
1. 用 Token A 创建一条测试事件，记录其 `id` (如 `event_del_123`)。
2. 调用软删除接口 `DELETE /api/life/events/event_del_123` 标记其已删除。
3. 尝试向该 ID 发送 PUT 修改请求：
   ```bash
   curl -i -X PUT http://localhost:5000/api/life/events/event_del_123 \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"title\": \"试图复活已删除数据\"}"
   ```
4. *预期响应*：`HTTP/1.1 404 Not Found`（绝不能返回 200 或允许修改已删除数据，杜绝半复活现象）。

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
2. 调用软删除接口。
3. 刷新前端 Timeline，确认事件不再返回。
4. 去 Firestore Console 检查对应的文档路径 `users/user_a/life_events/TEST_ID`，验证字段 `isDeleted = true` 且 `deletedAt` 被设为了当前 UTC 时间戳。

### 2. 标签单值筛选验证
1. 使用 Tag `骑行` 过滤：
   ```bash
   curl -X GET "http://localhost:5000/api/life/events?tag=%E9%AA%91%E8%A1%8C" \
     -H "Authorization: Bearer TOKEN_A"
   ```
2. 验证结果列表里的每一条记录其 `tags` 数组中均含有 `"骑行"`，且绝对不含其他用户的任何事件。

### 3. 非法 structuredData 格式校验 (422 Unprocessable Entity)
1. 发送包含非法格式的 `structuredData`（如不合法的 JSON 结构或不符合 schema 规定的属性）的 PUT 或 POST 接口：
   ```bash
   curl -i -X PUT http://localhost:5000/api/life/events/TEST_EVENT_ID \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"title\": \"测试非法数据\", \"structuredData\": \"not-an-object\"}"
   ```
2. *预期响应*：`HTTP/1.1 422 Unprocessable Entity`，响应体中包含错误码 `SCHEMA_VALIDATION_FAILED`。

---

## 四、 提醒闭环逻辑验证 (Phase 2B)

### 1. 明确提醒创建与原子双写验证
1. 调用 Ingest 接口（请求体使用 `text` 且必须包含 `clientTimeZone`）：
   ```bash
   curl -X POST http://localhost:5000/api/life/ingest \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"text\": \"明天下午3点提醒我给猫剪指甲\", \"clientTimeZone\": \"Asia/Shanghai\"}"
   ```
2. 检查 Firestore，确认对应的 `users/{uid}/reminders` 集合下产生了对应的物理提醒文档。
3. **原子性人工验证**：在调试代码中，强行在 Reminder 写入前注入异常，验证 Firestore 中 `LifeEvent` 和 `Reminder` 两端均未产生任何写入（没有出现孤立的 Event），证明 Transaction/Batch 机制工作正常。

### 2. 模糊提醒降级 (不写入 reminders)
1. 调用 Ingest 接口：
   ```bash
   curl -X POST http://localhost:5000/api/life/ingest \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"text\": \"以后记得提醒我买一本新书\", \"clientTimeZone\": \"Asia/Shanghai\"}"
   ```
2. 确认返回结果中 `parseStatus = "missing_due_time"`，检查 Firestore 仅生成了事件，绝对没有生成 Reminder。

### 3. 状态修改联动与互斥规则验证
1. **测试修改状态与 dueAt 互斥**：对于一个 pending 的提醒，尝试同时修改 `status` 与 `dueAt`：
   ```bash
   curl -i -X PATCH http://localhost:5000/api/reminders/TEST_REM_ID \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"status\": \"completed\", \"dueAt\": \"2026-06-26T09:00:00Z\"}"
   ```
   *预期响应*：`HTTP/1.1 400 Bad Request`。
2. **测试非 pending 状态下修改 dueAt**：将提醒修改为 completed 后，再次尝试修改该提醒的 `dueAt`：
   ```bash
   curl -i -X PATCH http://localhost:5000/api/reminders/TEST_REM_ID \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"dueAt\": \"2026-06-27T09:00:00Z\"}"
   ```
   *预期响应*：`HTTP/1.1 400 Bad Request`。

---

## 五、 每日总结逻辑验证 (Phase 2C)

### 1. 空自然日拦截返回验证 (无事件日)
1. 选择一个没有任何事件发生的空日期（或人为删除该天所有事件），例如 `2026-06-20`。
2. 触发 API：
   ```bash
   curl -X POST http://localhost:5000/api/daily-summaries/generate \
     -H "Authorization: Bearer TOKEN_A" \
     -H "Content-Type: application/json" \
     -d "{\"targetDate\": \"2026-06-20\", \"clientTimeZone\": \"Asia/Shanghai\"}"
   ```
3. *验证标准*：接口应当在数毫秒内直接秒回成功。返回的内容应精确匹配空状态结构：`summary = "这一天还没有记录。"`，`highlights = []`，`moodLabel = "暂无记录"`，`moodScore = null`，且在 `agent_runs` 执行日志中的 `status = "success"` 且 `inputEventCount = 0`，证明没有发生任何 LLM 请求。

### 2. 重复生成缓存规则验证
1. 在 2026-06-25 生成过一次总结之后，在不带 `forceRegenerate: true` 的情况下再次调用，验证结果秒回且内容一致。
2. 携带 `forceRegenerate: true` 调用，验证接口出现等待，并返回重新生成的总结。

### 3. LLM 异常与日志隔离验证
1. 故意给 LLM 传入错误指令或关闭 LLM API Key 配置，然后调用总结生成接口。
2. 确认 API 返回降级消息。查询 `users/{uid}/agent_runs` 集合，确认产生了一条记录。验证在 Firestore 对应文档的 `errorMsg` 中**仅存有脱敏简要错误信息**，而**绝不包含** Exception Stack Trace 堆栈（确保完整堆栈仅输出在后台 Cloud Logging 中）。

### 4. LLM 唠叨及 Markdown 代码块清洗验证
1. 在 Mock LLM 返回时，故意将响应文本格式设置为带有 markdown 包裹的乱码文本：
   ```
   Here is the extracted JSON object:
   ```json
   {
     "summary": "今天完成了日常骑行...",
     ...
   }
   ```
   Hope this helps!
   ```
2. 调用 Daily Summary API 触发生成，验证接口能够完美解析出该 JSON，无任何反序列化报错。

---

## 六、 自动化回归测试清单 (Automated Tests)

由于 Phase 2 逻辑日趋复杂，我们需要编写 xUnit 单元测试：
*   **`LlmHelperTest.cs`**：验证 `ExtractJsonObject(raw)` 提取函数。设计多组含有 markdown 标签、头部/尾部附带额外说明文字、换行等干扰字符的 raw 字符串，断言其提取后均能清洗并解析成正确的对象。
*   **`LifeEventServiceTest.cs`**：验证软删除逻辑只将 `isDeleted` 设为 `true` 并设置 `deletedAt`。验证基于游标的双字段排序（`occurredAt DESC, FieldPath.DocumentId DESC`）及游标翻页的下一页游标生成。
*   **`ReminderServiceTest.cs`**：测试提醒的状态转移限制：仅允许 `pending` -> `completed` / `cancelled`，禁止逆向修改。测试在 completed/cancelled 状态下修改 `dueAt` 或在变更状态为 completed/cancelled 时修改 `dueAt` 将被拒绝。
*   **`AgentRunnerTest.cs`**：测试时区边界转换的单元测试。

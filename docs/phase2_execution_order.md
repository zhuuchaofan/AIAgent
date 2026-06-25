# Phase 2 执行顺序与持续交付指南 (Execution Order)

为了保证系统始终处于“可工作”状态，Phase 2 严禁在一个巨大的特性分支中开发长达数周再集成。我们必须严格遵循以下小步快跑的执行顺序，并在每一步使用提供的 `curl` 命令进行快速验证。

---

## 严格执行流 (The Order)

### 阶段 0：Phase 2A-0 - 存量数据迁移
1. **(后端)** 编写仅限开发环境启用或带有强密钥鉴权的安全数据迁移 API，为 `users/{uid}/life_events` 下所有历史数据补足新字段。
   - 验证：运行迁移命令，检查 Console。
   - 迁移功能应支持 `dryRun`，单批次写入上限 500 次，且是幂等的。
   - Commit: `migration: add phase2a-0 data migration tool`
   - **验证 `curl` 命令**（通过 Header 携带校验密钥并使用 dryRun 测试）：
     ```bash
     curl -X POST "http://localhost:5000/api/migration/run-phase2a0?dryRun=true" \
       -H "Authorization: Bearer YOUR_TEST_TOKEN" \
       -H "X-Migration-Secret: YOUR_MIGRATION_SECRET"
     ```
     *预期响应*：
     ```json
     {
       "status": "success",
       "scannedCount": 42,
       "migratedCount": 0,
       "skippedCount": 0,
       "failedCount": 0
     }
     ```

### 第一阶段：Phase 2A - Timeline 增强
2. **(后端)** 修改 `LifeEvent` 模型，添加 `updatedAt`、`isDeleted` 和 `deletedAt`。
   - Commit: `feat: add soft delete fields to LifeEvent model`
3. **(后端)** 修改 GET `/api/life/events` 接口：过滤 `isDeleted == false`，支持单 `tag` 筛选，以及实现基于游标的分页（禁止 `offset`）。
   - Commit: `feat(api): support filtering isDeleted, tags, and cursor pagination in timeline`
   - **验证 `curl` 命令** (获取首屏, 限制每页 2 条，按标签筛选)：
     ```bash
     curl -X GET "http://localhost:5000/api/life/events?limit=2&tag=%E9%AA%91%E8%A1%8C" \
       -H "Authorization: Bearer YOUR_TEST_TOKEN"
     ```
     *预期响应*（返回 JSON 不含有任何注释，游标字段以 Base64 返回）：
     ```json
     {
       "items": [
         {
           "id": "event_1",
           "title": "今日骑行",
           "tags": ["骑行"]
         }
       ],
       "nextCursor": "eyJvY2N1cnJlZEF0IjoiMjAyNi0wNi0yNVQxNDo1MjoyOFoiLCJpZCI6ImV2ZW50XzEifQ=="
     }
     ```
4. **(后端)** 增加 PUT 与 DELETE `/api/life/events/{id}` 接口，处理子集合路径 `users/{uid}/life_events/{id}` 的强隔离。
   - Commit: `feat(api): add PUT and DELETE endpoints with subcollection isolation`
   - **修改 `curl` 命令**：
     ```bash
     curl -X PUT http://localhost:5000/api/life/events/event_1 \
       -H "Authorization: Bearer YOUR_TEST_TOKEN" \
       -H "Content-Type: application/json" \
       -d "{\"title\": \"更新后的骑行\", \"content\": \"骑行了 15km\", \"tags\": [\"骑行\", \"锻炼\"], \"importance\": 3, \"structuredData\": {}}"
     ```
   - **删除 `curl` 命令**：
     ```bash
     curl -X DELETE http://localhost:5000/api/life/events/event_1 \
       -H "Authorization: Bearer YOUR_TEST_TOKEN"
     ```
5. **(前端)** 实现编辑和删除的 UI 组件，对接 API，并在拉取 Timeline 时传递游标。
   - Commit: `feat(web): add edit and delete actions and cursor pagination to timeline`

### 第二阶段：Phase 2B - Reminder 提醒闭环
6. **(后端)** 新增 Reminder 相关模型，创建 `ReminderService` 与基本 API (GET/PATCH)。
   - Commit: `feat(api): add Reminder model and CRUD endpoints`
   - **拉取 Pending 提醒 `curl` 命令**：
     ```bash
     curl -X GET "http://localhost:5000/api/reminders?status=pending" \
       -H "Authorization: Bearer YOUR_TEST_TOKEN"
     ```
   - **更新提醒状态为已完成 `curl` 命令**（当状态变更为已完成时，不允许再修改 dueAt）：
     ```bash
     curl -X PATCH http://localhost:5000/api/reminders/reminder_1 \
       -H "Authorization: Bearer YOUR_TEST_TOKEN" \
       -H "Content-Type: application/json" \
       -d "{\"status\": \"completed\"}"
     ```
7. **(后端)** 修改 `GeminiLlmService` 提示词与 JSON 契约解析，调整 Ingest 接口实现 Event 与 Reminder 双写。
   - Commit: `feat(llm): extract reminder intent during ingest and write to firestore`
   - **验证双写 `curl` 命令**（原始文本属性为 `text`，必须携带 `clientTimeZone`）：
     ```bash
     curl -X POST http://localhost:5000/api/life/ingest \
       -H "Authorization: Bearer YOUR_TEST_TOKEN" \
       -H "Content-Type: application/json" \
       -d "{\"text\": \"明天早上 9 点提醒我吃感冒药\", \"clientTimeZone\": \"Asia/Shanghai\"}"
     ```
8. **(前端)** 新增 Reminder Widget 并在 Dashboard 呈现，实现状态 PATCH 交互。
   - Commit: `feat(web): implement Reminder widget on dashboard`

### 第三阶段：Phase 2C - Daily Summary 每日总结
9. **(后端)** 建立 `AgentRunner` 框架，支持基于客户端时区的自然日到 UTC 转换，编写调用 LLM 解析总结的逻辑，新增手动触发 API，实现基于 `forceRegenerate` 的幂等控制。
   - Commit: `feat(agent): implement daily summary generation endpoint with date window conversion`
   - **触发 Daily Summary 生成 `curl` 命令**（第一次生成）：
     ```bash
     curl -X POST http://localhost:5000/api/daily-summaries/generate \
       -H "Authorization: Bearer YOUR_TEST_TOKEN" \
       -H "Content-Type: application/json" \
       -d "{\"targetDate\": \"2026-06-25\", \"clientTimeZone\": \"Asia/Shanghai\", \"forceRegenerate\": false}"
     ```
     *预期响应*：
     ```json
     {
       "success": true,
       "data": {
         "id": "2026-06-25",
         "summary": "今天完成了日常骑行，并记录了猫咪的饮食状态，总体上是一个充实且健康的一天。",
         "highlights": [
           "完成了 15km 骑行",
           "记录了黑猫呕吐的日常"
         ],
         "moodLabel": "健康充实",
         "moodScore": 8,
         "suggestions": [
           "建议明天保持锻炼节奏",
           "注意观察猫咪后续反应"
         ]
       }
     }
     ```
10. **(前端)** 增加 Daily Summary 卡片，调用生成与拉取接口。
    - Commit: `feat(web): add daily summary display and manual generation button`

---

## 失败与回滚策略
- 每个部署点之前都会在本地进行手工/自动测试。
- 如果某次 Cloud Run 部署发现致命问题：
  - **立即回滚**：使用 `gcloud run services update-traffic life-agent-api --to-revisions=<上一稳定版本号>=100`。
  - **本地修复**：不在线上环境反复尝试，回滚后在本地跑通再重新走 CI/CD。

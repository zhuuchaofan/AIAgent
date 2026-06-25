# Phase 2 MVP 开发拆解与任务清单 (Tasks)

为了控制风险并保证系统稳定性，我们将 Phase 2 按照功能的独立闭环分为 2A-0, 2A, 2B, 2C 三个阶段，依次开发并验收。

---

## 🎯 Phase 2A-0: 存量数据迁移 (Data Migration)

### 任务 0.1: 存量 LifeEvents 数据迁移功能开发与执行
*   **目标**：为 Firestore 中已有的 `life_events` 补齐 `isDeleted=false`、`updatedAt`（设为当前时间或 `occurredAt`）、`deletedAt=null`。
*   **为什么不能直接 `WhereEqualTo("isDeleted", false)`**：Firestore 是一种无 Schema 的 NoSQL 数据库。若在查询条件中加入 `isDeleted == false`，Firestore **只会返回显式包含 `isDeleted` 字段且值为 `false` 的文档**。对于存量历史数据，由于没有这个字段，它们将被此查询过滤掉。因此，必须先进行全量迁移补齐字段，才能在 Timeline 查询中启用该过滤条件。
*   **安全与执行规范（极重要）**：
    *   **禁止无鉴权暴露**：迁移功能绝不能作为普通的、公开的生产业务 API 暴露。推荐通过本地 CLI 工具、Cloud Run 一次性 Job 或者是仅在开发环境下启用的 Controller 执行。
    *   **密钥鉴权校验**：如果采用 Controller 线上临时调用，必须设计严格的安全准入：强制校对特定的 `X-Migration-Secret` 请求头（比对本地环境变量 `MIGRATION_SECRET`），或者仅允许特定的管理员 Firebase UID 执行，否则一律返回 `403 Forbidden`。
    *   **业务逻辑特性**：
        *   **DryRun 支持**：支持传入参数 `?dryRun=true`，此时仅扫描数据库文档统计数量，绝不写入任何更改。
        *   **幂等性**：迁移操作必须是幂等的。若文档已存在 `isDeleted` 字段，直接跳过。
        *   **批处理写入**：采用 Firestore `WriteBatch` 机制进行分批写入，每批写入操作数量（`writes`）绝不能超过 Firestore 限制的 **500 次**。
        *   **指标输出**：迁移结束时，响应体必须结构化输出：`scannedCount`（总扫描数）、`migratedCount`（成功写入迁移数）、`skippedCount`（幂等跳过数）、`failedCount`（迁移失败数）。
*   **涉及文件**：新建 `MigrationController.cs` 或 `MigrationService.cs`（带有开发环境限制或安全密钥拦截）。
*   **验收方式**：
    *   执行迁移 API（带 Secret）。
    *   登录 Firestore Console 随机抽查 5 个历史文档，确认均已补齐上述三个字段。
    *   运行 GET `/api/life/events`，验证历史存量数据依然能够正常展示在 Timeline 中。

---

## 🎯 Phase 2A: Timeline 管理能力闭环

### 任务 1: 扩展 LifeEvent 模型，增加编辑与软删除标识
*   **目标**：在后端数据契约中支持编辑时间与软删除字段。
*   **涉及文件**：`LifeEvent.cs`
*   **实现细节**：新增 `updatedAt` (DateTime), `isDeleted` (bool), `deletedAt` (DateTime?)。
*   **验收方式**：编译通过。

### 任务 2: 修改 GET 接口过滤软删除记录与游标分页
*   **目标**：Timeline 默认不展示已删除的数据，并升级为强游标分页。
*   **涉及文件**：`LifeEventService.cs`
*   **实现细节**：
    *   在拉取分页查询中，增加 `.WhereEqualTo("isDeleted", false)` 过滤条件（依赖 2A-0 迁移的完成）。
    *   废除 `offset` 分页，在集合 `life_events` 上建立单集合复合索引，实现基于 `occurredAt` (DESC) + `id` (DESC) 的游标分页。利用 .NET SDK 的 `FieldPath.DocumentId` 进行显式二级排序或直接通过传入 `DocumentSnapshot` 起锚。
*   **验收方式**：被软删除的数据在 `/api/life/events` 中不再返回。

### 任务 3: 实现 Timeline 软删除 (DELETE)
*   **目标**：提供删除记录的接口，执行严格的用户隔离。
*   **涉及文件**：`LifeController.cs`, `LifeEventService.cs`
*   **实现细节**：增加 `DELETE /api/life/events/{id}`。由于使用子集合隔离路径 `users/{uid}/life_events/{id}`，必须根据当前 token 中解析出的 `uid` 访问该用户的子集合。若该 ID 不在当前用户的子集合中，Firestore 将直接返回未找到（404），从根本上杜绝越权。
*   **验收方式**：调用后该条目 `isDeleted` 变为 `true`。尝试跨用户删除会报 404。

### 任务 4: 实现 Timeline 编辑 (PUT)
*   **目标**：提供修改业务数据（title, content, tags, structuredData）的能力。
*   **涉及文件**：`LifeController.cs`, `LifeEventService.cs`
*   **实现细节**：增加 `PUT /api/life/events/{id}`。同样基于 `users/{uid}/life_events/{id}` 进行更新，防止覆盖系统字段。
*   **验收方式**：能成功修改 title 或 tags，更新 `updatedAt`，且跨用户修改会报 404。

### 任务 5: 前端 Timeline 交互增强
*   **目标**：前端出现编辑和删除入口。
*   **实现细节**：卡片右侧新增下拉菜单，点击编辑弹出表单回显，点击删除出现二次确认。

### 任务 6: 增加单 tag 筛选
*   **目标**：支持在时间轴上快速按标签查找。
*   **涉及文件**：`LifeController.cs`, `LifeEventService.cs`
*   **实现细节**：GET 请求接受 `tag` Query 参数，应用 `array-contains` 过滤。前端增加 Tag 选择栏。

---

## 🎯 Phase 2B: Reminder 提醒系统闭环

### 任务 7: 新增 Reminder 模型和 Service
*   **目标**：基础数据访问层建设。
*   **涉及文件**：`Reminder.cs`, `ReminderService.cs`
*   **实现细节**：依据 `phase2_firestore_schema.md` 创建模型及基本的 CRUD 方法，数据存储在 `users/{uid}/reminders/{id}`。其中 `dueAt` 设为必填（不可为 null），并且增加 `repeatRule` 字段（默认 `"none"`）。

### 任务 8: 修改 GeminiLlmService，支持输出 Reminder 结构
*   **目标**：LLM 能够按照新的 Contract 同时返回事件提取与提醒意图。
*   **涉及文件**：`GeminiLlmService.cs`, `ParsedEvent.cs`
*   **实现细节**：更新 Prompt，添加 `reminder` JSON 节点，处理时间格式转换及降级逻辑（`missing_due_time`）。

### 任务 9: 修改 Ingest，支持 LifeEvent 与 Reminder 双写
*   **目标**：一次保存，双表落库。
*   **涉及文件**：`LifeController.cs`, `LifeEventService.cs`
*   **实现细节**：
    *   如果 LLM 返回了 `parseStatus=success` 且带有合法时间的 reminder 意图，则在同一个操作中向 `users/{uid}/reminders` 写入物理提醒条目。
    *   如果 `parseStatus` 为 `missing_due_time` / `invalid_due_time` / `llm_parse_failed`，或者 `dueAtIso8601` 解析为空，后端将**仅创建 LifeEvent，绝不创建 Reminder 物理实体**。
    *   接收请求时使用向后兼容的 `text` 属性，校验 `clientTimeZone`，如果缺失采用默认 `Asia/Shanghai` 且在 `structuredData` 写入 `timezoneFallbackUsed: true` 审计日志。

### 任务 10: 新增 Reminder API
*   **目标**：前端能够获取与操作提醒。
*   **涉及文件**：`ReminderController.cs`
*   **实现细节**：
    *   增加 GET, PATCH 接口。
    *   PATCH 仅在提醒状态为 `pending` 且请求不修改状态时允许修改 `dueAt`。
    *   如果请求修改状态为 `completed` / `cancelled`，则不允许修改 `dueAt`。对于已经是 `completed`/`cancelled` 状态的提醒，拒绝修改 `dueAt` 并返回 `400 Bad Request`。

### 任务 11: 前端新增 Reminder Widget
*   **目标**：在前端 Dashboard 侧边栏展示 Pending 状态的提醒。

---

## 🎯 Phase 2C: Daily Summary 每日总结闭环

### 任务 12: 新增 DailySummary 模型
*   **涉及文件**：`DailySummary.cs`，存储在 `users/{uid}/daily_summaries/{dateStr}`，其中 `dateStr` 格式为 `YYYY-MM-DD`。

### 任务 13: 新增 AgentRunner 核心引擎与缓存/重复生成控制
*   **目标**：提供捞取数据 -> 组装 Prompt -> 调用大模型生成总结的能力，同时实现缓存控制。
*   **涉及文件**：`AgentRunner.cs`, 对应的 Prompt 配置。
*   **实现细节**：
    *   接收前端传入的本地目标日期（`targetDate`，如 "2026-06-25"）、客户端时区（`clientTimeZone`）以及 `forceRegenerate` 标识。
    *   **重复生成控制**：查询是否已存在 `users/{uid}/daily_summaries/{targetDate}`。如果已存在且 `forceRegenerate != true`，直接将已有数据返回，不重复调用 LLM；如果 `forceRegenerate == true`（或文档不存在），才继续执行调用 LLM 生成并覆盖旧文档。
    *   计算出该自然日的本地起止时间（`00:00:00` 至次日零点），并将起止时间转换为 UTC 时间（`startUtc` 和 `endUtc`）。
    *   在 `users/{uid}/life_events` 下，查询满足 `occurredAt >= startUtc && occurredAt < endUtc` 且 `isDeleted == false` 的所有事件。
    *   调用 LLM 生成 JSON 结构的总结，同时在 `users/{uid}/agent_runs` 记录执行日志（记录 Prompt 哈希和部分预览，脱敏简要错误，完整堆栈仅写入 Cloud Logging）。

### 任务 14: 新增手动触发总结 API
*   **涉及文件**：`AgentController.cs`
*   **实现细节**：暴露 POST `/api/daily-summaries/generate` 接口触发 AgentRunner。

### 任务 15: 前端展示 Daily Summary
*   **目标**：在前端展示最近一天的情绪状态与高光时刻。

# LifeOS Phase 2 核心路线图 (Roadmap)

## 一、 Phase 2 总目标
Phase 2 的核心目标是从“单纯的记录”（Phase 1）向“智能助理”转变。我们将引入时间轴的交互能力（编辑/删除/筛选），实现自然语言驱动的提醒闭环（Reminder），以及每日数据的聚合反馈（Daily Summary），为用户提供闭环的情绪与事项管理。

## 二、 Phase 2 与 Phase 1 的区别
- **Phase 1** 侧重于单向的数据摄入（Ingest）、基础的 Timeline 流水账展示，以及整体云原生架构与鉴权链路的跑通。
- **Phase 2** 侧重于数据的交互闭环与生命周期管理，增加了提醒意图的解析与持久化，并将引入基于事件的二次总结能力，让“记忆”发挥价值。

## 三、 Phase 2 阶段划分与范围

### 🎯 Phase 2A-0：存量数据迁移任务
**功能目标**：为 Phase 1 的历史数据补齐 Phase 2 所需的生命周期管理字段。
- **背景与原因**：
  在 Phase 2 中，为了过滤软删除数据，查询事件列表时必须加入 `isDeleted == false` 的过滤条件。由于 Firestore 采用**无模式/稀疏索引（Sparse Index）**机制，如果文档在数据库中完全缺失 `isDeleted` 字段，它是**绝不可能**被任何包含 `isDeleted == false` 条件的查询匹配到的。Firestore 不支持对缺失字段进行隐式的默认值匹配（即不能直接通过 `.WhereEqualTo("isDeleted", false)` 查询出缺失该字段的历史旧数据）。因此，我们必须补齐存量文档。
- **安全与迁移规范**：
  - 开发带安全密钥校验（比对 `MIGRATION_SECRET`）或仅在开发环境启用的安全数据迁移接口。
  - 迁移任务必须是**幂等**的，必须支持 **dryRun**，并且必须使用分批写入（`WriteBatch`）确保单次提交不超过 **500 次** Firestore 写入限制。
  - 迁移结束后输出 `scannedCount`, `migratedCount`, `skippedCount`, `failedCount` 审计指标。
- **迁移验收方式**：
  1. 通过数据库控制台（Firestore Console）查询，确认旧有文档上已显式出现了上述新字段。
  2. 使用带 `isDeleted == false` 条件的 API 请求，能够成功返回未被修改的存量历史数据。

### 🎯 Phase 2A：Timeline 管理能力
**功能目标**：补齐 LifeEvent 的生命周期管理。
- **编辑 LifeEvent**：允许用户修正大模型提取错误的标签或属性。
- **软删除 LifeEvent**：提供数据的后悔药，删除后不再展示，但数据在数据库中保留（isDeleted 标志）。
- **单标签筛选 Timeline**：通过提取出的 tags 对 Timeline 进行快速过滤。

### 🎯 Phase 2B：Reminder 提醒闭环
**功能目标**：自然语言转化为可追踪的待办事项。
- **LLM 解析提醒意图与向后兼容**：
  - Ingest 请求继续使用向后兼容的 `text` 字段（而非 `rawText`），并强制携带 `clientTimeZone` 以实现准确的时间换算。若缺失采用 `Asia/Shanghai` 时区降级并记录 `timezoneFallbackUsed: true` 审计日志。
  - 识别用户“明天提醒我xxx”的诉求并由 LLM 格式化时间。
- **reminders 集合落库**：仅在 `parseStatus = "success"` 且 `dueAt` 时间明确时创建 Reminders 文档（默认补齐 `repeatRule: "none"`）。
- **Reminder Widget 展示**：前端新增专门区域展示提醒。
- **Reminder 状态流转限制**：支持将提醒标记为 completed 或 cancelled（单向状态机，一旦流转则不再恢复为 pending）。**状态变更为已完成/取消时，不允许同时修改 `dueAt`；已完成/取消状态下禁止修改 `dueAt`**。
- **Web 内状态呈现**：基于 dueAt 和 status 动态推导并显示 pending / overdue / completed / cancelled。

### 🎯 Phase 2C：Daily Summary
**功能目标**：实现“日反思”与聚合分析。
- **手动触发每日总结与缓存幂等**：由前端通过 `POST /api/daily-summaries/generate` 手动触发。如果当前日期总结已生成且 `forceRegenerate != true`，直接返回已生成数据而不重复调用 LLM。
- **本地自然日时间窗口**：总结不再以“过去 24 小时”作为时间窗。而是严格依据入参中的目标本地日期 `targetDate` 与客户端时区 `clientTimeZone`，将其推导为本地自然日的 `00:00:00` 至次日 `00:00:00`，转换成 UTC 边界 `startUtc` 和 `endUtc`，并采用 `occurredAt >= startUtc && occurredAt < endUtc` 进行过滤查询。
- **daily_summaries 集合落库**：总结结果落库持久化，以 `targetDate` 字符串作为文档 ID（如 `2026-06-25`）。
- **Dashboard 展示**：前端增加总结展示卡片。
- **agent_runs 记录执行日志**：安全记录执行过程与 LLM 返回概要。Firestore 仅记录脱敏后的简要错误信息，完整 Exception Stack Trace 仅存入后台 Cloud Logging 避免安全泄露。

### 🎯 Phase 2 Plus（本阶段主线不做，后续延伸）
- **Cloud Scheduler 自动触发**：使用后台专用的内部接口 `/internal/agent/jobs/daily-summary` 通过定时任务触发每日总结。
- **外部通知推送**：接入 Telegram / PushDeer / Web Push 推送提醒和总结。
- **异步 Agent**：使用 Firestore Trigger 或 Eventarc 实现真正的事件驱动异步总结。

## 四、 明确本阶段不做的事情 (Non-Goals)
*不涉及外部通知推送、不包含后台自动化定时运行（不接入 Cloud Scheduler）、不包含复杂的多条件或多标签组合筛选。详细见 `phase2_non_goals.md`。*

## 五、 最终完成标准
1. **API 闭环**：所有新增接口（编辑、删除、单标签查询、提醒 CRUD、单用户总结生成）调通并具备严格的 UID 隔离。
2. **前后端集成**：Timeline 上可以顺畅地编辑和删除条目；输入提醒意图能同步产生并在前端 Widget 呈现 Reminder；可以手动点击按钮生成当天的总结。
3. **数据安全**：软删除生效（API 和界面不返回）；任何操作均无法越权访问他人数据；敏感数据不落库。
4. **存量迁移**：Phase 1 所有历史数据已完成 isDeleted 补齐，在 Timeline 列表中可无缝呈现。

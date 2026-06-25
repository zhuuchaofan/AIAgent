# Phase 2 Firestore 数据结构规范 (Schema Spec)

## 一、 数据隔离与查询原则
所有用户产生的数据，包括 `life_events`、`reminders`、`daily_summaries`、`agent_runs` 等，**一律使用子集合路径进行隔离**（即 `users/{uid}/...`）。
- 文档内部包含的 `userId` 仅作为冗余、审计以及未来跨集合（Collection Group）查询时的兜底字段。
- **普通查询绝不依赖 `userId == x` 作为主要过滤条件**。
- **索引配置必须基于集合 ID（Collection ID）级别配置为单集合（Single Collection）查询模式**，绝不能为每一个具体的“用户子集合”单独建立索引。同时也不需要引入 `userId` 的复合索引（除非明确使用 collectionGroup 跨用户查询）。

---

## 二、 集合与字段规范

### 1. 长期生活事件 (users/{userId}/life_events/{eventId})
**在 Phase 1 基础上扩展的字段：**
| 字段名 | 类型 | 必须 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `updatedAt` | Timestamp | 是 | `createdAt` 的值 | 记录最后一次被编辑的时间 |
| `isDeleted` | Boolean | 是 | `false` | 软删除标识，查询 Timeline 时默认过滤 `!= true` |
| `deletedAt` | Timestamp | 否 | `null` | 记录被软删除的 UTC 时间 |
| `reminderIntentDetected` | Boolean | 是 | `false` | 是否包含提醒意图 |
| `reminderParseStatus` | String | 是 | `"none"` | 提醒解析状态（见下方枚举定义） |
| `reminderParseNote` | String | 否 | `null` | 解析状态备注或错误信息 |
| `createdReminderId` | String | 否 | `null` | 若成功创建提醒，关联的 Reminder ID |

**reminderParseStatus 枚举值定义：**
- `none`: 未检测到提醒意图或不触发解析。
- `success`: 成功检测并解析出提醒时间，已成功关联创建 Reminder。
- `missing_due_time`: 检测到提醒意图但完全缺失时间（如“以后记得提醒我”），无法创建具体 Reminder 记录。
- `invalid_due_time`: 检测到提醒意图，但 LLM 给出或解析出了无法识别/不合法的时间格式。
- `llm_parse_failed`: 提取意图失败、LLM 输出 JSON 严重损坏或底层调用报错。

**软删除规则**：
删除操作一律更新 `isDeleted = true` 和 `deletedAt = now`，不实际执行物理 Delete。需要注意，历史存量数据（Phase 1 遗留）必须通过跑批数据迁移补齐这些字段，否则 Firestore 的 `.WhereEqualTo("isDeleted", false)` 无法匹配到它们。

### 2. 提醒事项 (users/{userId}/reminders/{reminderId})
**物理 Reminder 创建条件**：物理 Reminder 文档**仅在** Ingest 阶段识别到提醒意图，且 `parseStatus = "success"` 并且 `dueAtIso8601` 存在合法的时间时才会被创建。若 `parseStatus` 为 `missing_due_time` / `invalid_due_time` / `llm_parse_failed` 时，**仅创建 LifeEvent，绝不创建 Reminder 物理实体**。

**完整字段设计：**
| 字段名 | 类型 | 必须 | 说明 |
| :--- | :--- | :--- | :--- |
| `id` | String | 是 | 提醒的唯一 ID |
| `userId` | String | 是 | 冗余审计字段：所属用户 ID（不作为普通查询的首要过滤字段） |
| `sourceEventId` | String | 是 | 触发此提醒的原始 LifeEvent ID |
| `title` | String | 是 | 提醒标题 |
| `description` | String | 否 | 提醒详细描述 |
| `dueAt` | Timestamp | 是 | 提醒到期的 UTC 时间（因为只在 success 时创建，此处设为必填，绝不能为 null） |
| `timezone` | String | 是 | 创建提醒时的客户端时区 |
| `status` | String | 是 | 状态：`pending`, `completed`, `cancelled` |
| `repeatRule` | String | 是 | 循环提醒规则：目前在 Phase 2 固定传入 `"none"`，代表单次提醒，不进行循环。为后续版本做准备。 |
| `createdAt` | Timestamp | 是 | 创建时间 |
| `updatedAt` | Timestamp | 是 | 最后更新时间 |
| `completedAt` | Timestamp | 否 | 完成时间 |
| `cancelledAt` | Timestamp | 否 | 取消时间 |
| `llmConfidence` | Double | 是 | LLM 提取此提醒意图的置信度 |
| `rawText` | String | 是 | 产生该提醒的原始输入句子（即 Ingest 的 `text` 字段内容） |

**状态机与 Overdue 判定**：
- **状态流转规则**：
  - 允许流转路径：
    - `pending` -> `completed` (自动设置 `completedAt = UTC Now`，`updatedAt = UTC Now`)
    - `pending` -> `cancelled` (自动设置 `cancelledAt = UTC Now`，`updatedAt = UTC Now`)
  - **单向不可逆性**：一旦状态变为 `completed` 或 `cancelled`，在 Phase 2 中是不支持恢复为 `pending` 的。
- **Overdue (超期) 判定**：
  - `overdue` 不作为落库的 status 物理状态，保持 `pending` 物理属性。
  - 列表接口（以及前端渲染）通过动态逻辑判断：若当前服务器时间 `UTC Now > dueAt` 且 `status == "pending"`，则在 response 中动态返回展示字段 `displayStatus = "overdue"`。

### 3. 每日总结 (users/{userId}/daily_summaries/{date})
**文档 ID 规范**：使用 `targetDate` 字符串作为文档 ID（例如 `2026-06-25`）。
**时间窗口范围**：严格对应 `targetDate + clientTimeZone` 所在的本地自然日。通过把本地自然日 `targetDate` 的 `00:00:00` 至 `24:00:00` 换算成 UTC 边界 `startUtc` 和 `endUtc`，过滤查询 `occurredAt >= startUtc && occurredAt < endUtc`。

**完整字段设计：**
| 字段名 | 类型 | 必须 | 说明 |
| :--- | :--- | :--- | :--- |
| `id` | String | 是 | 日期字符串，同文档 ID（如 "2026-06-25"） |
| `userId` | String | 是 | 冗余审计字段：所属用户 ID |
| `summary` | String | 是 | 总体概览总结 |
| `highlights` | Array | 是 | 当日高光事件数组 |
| `moodLabel` | String | 是 | 情绪标签 |
| `moodScore` | Integer | 是 | 综合情绪得分 (1-10) |
| `suggestions` | Array | 是 | 针对次日的建议 |
| `sourceEventIds` | Array | 是 | 用于生成此总结的源事件 ID 列表 |
| `createdAt` | Timestamp | 是 | 生成时间 |

### 4. Agent 执行日志 (users/{userId}/agent_runs/{runId})
**日志脱敏与精简原则**：出于存储成本与安全考量，生产环境默认**绝对不保存**完整的 prompt 文本（`promptUsed`）。仅当配置了环境变量 `SAVE_FULL_AGENT_PROMPT=true` 时（建议仅在开发/调试环境），才落库完整 prompt 数据。

**完整字段设计：**
| 字段名 | 类型 | 必须 | 说明 |
| :--- | :--- | :--- | :--- |
| `id` | String | 是 | 运行日志 ID |
| `userId` | String | 是 | 冗余审计字段：所属用户 ID |
| `jobType` | String | 是 | 任务类型（如 `"daily_summary"`） |
| `status` | String | 是 | 执行状态：`success`, `failed` |
| `model` | String | 是 | 使用的大模型版本（如 `"gemini-3.5-pro"`） |
| `promptHash` | String | 否 | Prompt 的哈希值（用于追溯与版本辨识） |
| `promptPreview` | String | 否 | Prompt 的前 100 字符预览 |
| `inputEventCount` | Integer | 是 | 喂给模型的上下文事件数量 |
| `sourceEventIds` | Array | 是 | 喂给模型的事件 ID 列表 |
| `rawResponsePreview` | String | 否 | LLM 返回的原始 JSON 文本预览 |
| `errorMsg` | String | 否 | 报错时的简要脱敏错误信息（完整 Exception Stack Trace 仅写入后端 Cloud Logging 中以防隐私泄露，绝对不落库 Firestore） |
| `startedAt` | Timestamp | 是 | 开始时间 |
| `endedAt` | Timestamp | 是 | 结束时间 |

---

## 三、 查询模式与索引需求 (Collection 级别)

由于 Firestore 索引是基于**集合 ID（Collection ID）**配置的，对子集合运行的查询也是通过在该集合 ID 上建立单集合（Single Collection）索引来支持的。我们不需要也无法为每个用户的特定子集合单独配置索引，同时也不需要包含 `userId` 的复合索引（除非跨用户使用 Collection Group 查询）。

1. **LifeEvent 查询**（针对集合 ID `life_events` 单集合索引）：
   - 模式 A（默认 Timeline 列表）：`isDeleted == false` ORDER BY `occurredAt` DESC, `__name__` DESC
   - 模式 B（Tag 过滤）：`isDeleted == false` AND `tags array-contains "tag"` ORDER BY `occurredAt` DESC, `__name__` DESC
   - **单集合索引配置**：
     *   复合索引 1：集合 ID = `life_events`，字段列表 = `isDeleted (ASC) + occurredAt (DESC) + __name__ (DESC)`，范围 = Collection
     *   复合索引 2：集合 ID = `life_events`，字段列表 = `tags (array-contains) + isDeleted (ASC) + occurredAt (DESC) + __name__ (DESC)`，范围 = Collection

2. **Reminder 查询**（针对集合 ID `reminders` 单集合索引）：
   - 模式（默认拉取）：`status == "pending"` ORDER BY `dueAt` ASC
   - **单集合索引配置**：
     *   复合索引：集合 ID = `reminders`，字段列表 = `status (ASC) + dueAt (ASC)`，范围 = Collection

### 游标排序中的 `__name__` 分页在 .NET 中的实现机制
在 .NET Firestore SDK 中实现 `occurredAt DESC, __name__ DESC` 的双字段排序及游标翻页，有以下两种规范写法：
1.  **推荐方法（基于 DocumentSnapshot 锚点）**：
    在查询时使用 `StartAfter(lastDocumentSnapshot)` 方法。只需要使用 `OrderByDescending("occurredAt").OrderByDescending(FieldPath.DocumentId)` 组织查询，在分页时直接把上一页最后一条记录的 `DocumentSnapshot` 传入作为游标。Firestore SDK 会自动提取该 snapshot 中的 `occurredAt` 属性和其 Document ID 并在服务端执行定位，无需手动反序列化出具体的值。
2.  **备用方法（基于显式游标值）**：
    在游标 JSON 中反序列化出上一次最后一条记录的 `occurredAt` 字符串和 `id`，然后在查询构建中显式添加排序和游标起始值：
    ```csharp
    query.OrderByDescending("occurredAt")
         .OrderByDescending(FieldPath.DocumentId)
         .StartAfter(occurredAtUtc, lastDocumentId);
    ```
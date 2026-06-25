# Phase 2 API 接口规范 (API Spec)

## 通用鉴权与隔离规则
1. 所有接口必须在 Header 中携带 `Authorization: Bearer <Firebase_ID_Token>`。
2. 后端验证中间件将强制解析 UID，挂载为当前上下文 `userId`（或 `uid`）。**绝对不允许**信任任何由前端 Request Body 或 Query String 传入的 `userId`。
3. 所有数据操作均限制在当前用户的子集合路径下（例如 `users/{uid}/life_events/...`，`users/{uid}/reminders/...`）。由于路径天然隔离，后端将直接对当前登录用户的子集合进行操作。
4. 任何涉及特定资源 ID 的操作（如 `GET`, `PUT`, `DELETE`, `PATCH` 对特定 `{id}` 的操作），后端均需在当前用户的子集合内定位该文档。如找不到或跨用户非法请求，为防枚举，**一律返回 `404 Not Found`**（或显式返回 `403 Forbidden`）。
5. 未携带 Token 或 Token 无效、过期，**一律返回 `401 Unauthorized`**。

---

## 零、 迁移相关 API (Migration - 极低频/一次性)

### 1. 存量数据字段补齐迁移 (POST /api/migration/run-phase2a0)
*   **路径**：`POST /api/migration/run-phase2a0`
*   **功能**：对现有 Phase 1 的所有存量 life_events 补足 isDeleted/updatedAt/deletedAt 字段。
*   **生命周期约束**：
    该 API 为一次性数据迁移接口。迁移完成并验证通过后，迁移 Controller 的路由**必须从生产环境中移除，或通过环境变量 `ENABLE_MIGRATION_API=false` 默认禁用**。禁止在生产环境长期暴露此接口，以防给自己挖坑。
*   **请求安全头**：需要验证 Header 中的 `X-Migration-Secret`，其值必须与后端的 `MIGRATION_SECRET` 环境变量严格相符。
*   **Request Query**：
    `dryRun` (可选，Boolean，默认 `false`。若为 `true` 则仅统计不写入更改)
*   **Response** (`200 OK`)：
    *dryRun=true 模式下：`migratedCount` 必为 0，`wouldMigrateCount` 统计预计修改量；非 dryRun 模式下：`migratedCount` 记录实际写入量，`wouldMigrateCount` 为 0。*
    ```json
    {
      "success": true,
      "scannedCount": 42,
      "migratedCount": 35,
      "wouldMigrateCount": 0,
      "skippedCount": 7,
      "failedCount": 0
    }
    ```

---

## 一、 Timeline 相关 API (LifeEvent)

### 1. 摄入事件并提取 (POST /api/life/ingest)
*   **路径**：`POST /api/life/ingest`
*   **功能**：对用户原始文本进行摄入，并由大语言模型执行实体提取。
*   **请求兼容字段说明**：
    为保持与 Phase 1 现有代码与前端组件的完全向后兼容，请求体统一使用 `text` 字段，绝对不使用 `rawText`。
*   **请求时区说明**：
    请求体中必须包含 `clientTimeZone` 字段以支持 Reminder 的准确时间换算。若缺失，后端将采用默认时区 `Asia/Shanghai`，并在生成的 LifeEvent `structuredData` 元数据中记录 `timezoneFallbackUsed: true`。
*   **Request Body**：
    ```json
    {
      "text": "明天早上 9 点提醒我吃感冒药",
      "clientTimeZone": "Asia/Shanghai"
    }
    ```
*   **Response** (`200 OK`)：
    ```json
    {
      "success": true,
      "data": {
        "eventId": "event_123",
        "title": "吃感冒药",
        "reminderId": "rem_123"
      }
    }
    ```

### 2. 获取事件列表 (GET /api/life/events)
- **路径**：`GET /api/life/events`
- **Query 参数**：
  - `limit` (可选，整数，默认 20)
  - `cursor` (可选，Base64 编码的 JSON 字符串，表示分页游标)
  - `tag` (可选，单标签筛选，如 `tag=骑行`)
- **后台处理与游标规范**：
  - **明确禁止使用 Offset 分页**。由于 Firestore 机制，必须使用游标（Cursor）翻页。
  - **双字段排序规则**：列表必须严格按照 `occurredAt` DESC + `id` DESC（文档名 `__name__` DESC）进行双字段组合排序，以确保排序稳定性。
  - **游标格式与生命周期**：
    - `cursor` 是一个由客户端传入 of Base64 编码的 JSON 字符串。
    - 其 JSON 结构定义为：
      ```json
      {
        "occurredAt": "2026-06-25T14:30:00Z",
        "id": "eventId_123"
      }
      ```
    - 后端在反序列化 cursor 后，作为 Firestore `StartAfter` 查询的起始锚点。
  - 默认应用过滤条件 `isDeleted == false`。
  - 若传入 `tag`，则应用 `tags array-contains {tag}` 过滤条件。
- **Response 字段额外说明**：
  *   `nextCursor`：Base64 编码的 JSON 游标，当没有下一页数据时，该字段返回 `null`。
- **Response** (`200 OK`)：
  ```json
  {
    "success": true,
    "nextCursor": "eyJvY2N1cnJlZEF0IjoiMjAyNi0wNi0yNVQxNDozMDowMFoiLCJpZCI6ImV2ZW50SWRfMTIzIn0=",
    "data": [
      {
        "id": "eventId_123",
        "title": "今天去骑行",
        "content": "在东湖绿道骑行了 15 公里",
        "occurredAt": "2026-06-25T14:30:00Z",
        "createdAt": "2026-06-25T14:31:00Z",
        "updatedAt": "2026-06-25T14:31:00Z",
        "tags": ["骑行", "运动"],
        "source": "telegram",
        "importance": 3,
        "structuredData": {}
      }
    ]
  }
  ```

### 3. 更新事件 (PUT /api/life/events/{id})
- **路径**：`PUT /api/life/events/{id}`
- **功能**：更新事件的业务数据。
- **Request Body**：
  ```json
  {
    "title": "更新后的标题",
    "content": "更新后的内容",
    "tags": ["新标签"],
    "structuredData": {}
  }
  ```
- **后台处理与删除防御**：
  - 在当前用户的子集合 `users/{uid}/life_events` 中检索 `{id}` 文档。若该事件不属于该用户（即不存在于该路径），直接返回 `404 Not Found`。
  - **已软删除的事件禁止修改**：如果检索到的事件文档中 `isDeleted == true`，**直接返回 `404 Not Found`**，绝不允许对其进行任何修改，从根本上防止因前端缓存残留导致的“半复活”现象。
  - 仅覆盖业务字段（如 title, content, tags, structuredData）。
  - **严禁**前端修改系统控制字段（如 `id`, `userId`, `createdAt`, `source` 等）。
  - 自动将 `updatedAt` 设为当前服务器 UTC 时间。
- **Response** (`200 OK`)：
  ```json
  {
    "success": true,
    "message": "更新成功"
  }
  ```

### 4. 软删除事件 (DELETE /api/life/events/{id})
- **路径**：`DELETE /api/life/events/{id}`
- **后台处理**：
  - 在当前用户的子集合 `users/{uid}/life_events` 中检索 `{id}`。若不存在直接返回 `404 Not Found`。
  - **绝不执行物理删除**。更新该文档字段：
    - `isDeleted = true`
    - `deletedAt = UTC Now`
    - `updatedAt = UTC Now`
- **Response** (`200 OK`)：
  ```json
  {
    "success": true,
    "message": "事件已成功软删除"
  }
  ```

---

## 二、 提醒相关 API (Reminders)

### 1. 获取提醒列表 (GET /api/reminders)
- **路径**：`GET /api/reminders`
- **Query 参数**：
  - `status` (可选，字符串。取值：`pending`, `completed`, `cancelled`。若不传默认返回 `pending`)
- **后台处理**：
  - 从当前用户的子集合 `users/{uid}/reminders` 中拉取对应状态的提醒列表。
  - 按照 `dueAt` ASC 排序。
  - **Overdue 动态判定与 displayStatus 字段说明**：
    - 数据库中**不保存** `overdue` 状态值。
    - 后端在返回响应时，判断若 `status == "pending"` 且 `dueAt < DateTime.UtcNow`，则在 Response 中将 `displayStatus` 设为 `"overdue"`。其他情况下，`displayStatus` 与 `status` 保持相同值。
- **Response** (`200 OK`)：
  ```json
  {
    "success": true,
    "data": [
      {
        "id": "rem_123",
        "sourceEventId": "eventId_123",
        "title": "提醒我喝水",
        "description": "多喝水有益健康",
        "dueAt": "2026-06-25T09:00:00Z",
        "timezone": "Asia/Tokyo",
        "status": "pending",
        "displayStatus": "overdue",
        "repeatRule": "none",
        "createdAt": "2026-06-25T08:00:00Z",
        "updatedAt": "2026-06-25T08:00:00Z"
      }
    ]
  }
  ```

### 2. 更新提醒状态/时间 (PATCH /api/reminders/{id})
- **路径**：`PATCH /api/reminders/{id}`
- **功能**：修改提醒的状态（标记为完成或取消）和/或直接更新提醒的时间（dueAt）。
- **Request Body**：
  ```json
  {
    "status": "completed", 
    "dueAt": "2026-06-26T09:00:00Z" 
  }
  ```
  *(注：`status` 与 `dueAt` 均为可选入参，但不可同时为空。)*
- **后台处理与状态流转及联动限制规则**：
  - 在当前用户的子集合 `users/{uid}/reminders` 中检索 `{id}` 文档。若不存在，直接返回 `404 Not Found`。
  - **允许的状态流转规则**：
    - 仅允许由 `pending` 状态流转到 `completed` 或 `cancelled` 状态。
    - **单向不可逆**：状态一旦变更为 `completed` 或 `cancelled`，在 Phase 2 中**绝对不支持**恢复为 `pending` 状态。若传入 `"status": "pending"`，接口直接拒绝并返回 `400 Bad Request`。
  - **时间与状态修改互斥规则**：
    - 当请求试图将状态修改为 `completed` 或 `cancelled` 时，**绝对不允许同时修改 `dueAt`**。
    - 只有当提醒的当前状态是 `pending`（且请求没有将状态变更为完成或取消）时，才允许更新 `dueAt`。
    - 任何对于已经是 `completed` 或 `cancelled` 状态的提醒，再次尝试更新其 `dueAt` 的请求，均返回 `400 Bad Request`。
  - **属性联动**：
    - 若状态流转为 `completed`：自动设置 `completedAt = UTC Now`，`updatedAt = UTC Now`。
    - 若状态流转为 `cancelled`：自动设置 `cancelledAt = UTC Now`，`updatedAt = UTC Now`。
    - 若仅修改了 `dueAt`：保持 `status` 不变，更新 `dueAt` 与 `updatedAt = UTC Now`。
- **Response** (`200 OK`)：
  ```json
  {
    "success": true,
    "message": "提醒更新成功"
  }
  ```

---

## 三、 每日总结 API (Daily Summary)

### 1. 生成当前用户每日总结 (POST /api/daily-summaries/generate)
- **路径**：`POST /api/daily-summaries/generate`
- **功能**：前端或手动触发，**仅限为当前登录用户（基于 uid 隔离）**生成目标自然日的每日总结。
- **Request Body**：
  ```json
  {
    "targetDate": "2026-06-25", 
    "clientTimeZone": "Asia/Tokyo",
    "forceRegenerate": false
  }
  ```
- **重复生成与幂等规则**：
  - 当请求到达时，后端会先查询路径 `users/{uid}/daily_summaries/{targetDate}` 下对应的总结文档是否存在。
  - 若已存在且传入的 `forceRegenerate` 不为 `true`（或缺省），**将不重复调用大语言模型（LLM）**，而是直接从数据库中读取并返回该总结文档以防范 Token 浪费。
  - 若已存在且传入了 `forceRegenerate` 且其值为 `true`，则启动 LLM 解析流程，重新调用 LLM 生成并覆盖旧的总结文档。
  - 不管是首次生成还是强制覆盖，均会在 `users/{uid}/agent_runs` 写入一条执行日志。
- **核心时间窗口处理（自然日边界）**：
  - 废除“过去 24 小时”的滑动窗口概念，采用**本地自然日**时间窗口。
  - **本地边界推导**：
    - `startLocal` = `targetDate` 00:00:00 (时区为 `clientTimeZone`)
    - `endLocal` = `targetDate` + 1 day 00:00:00 (即次日零点，时区为 `clientTimeZone`)
  - **UTC 边界转换**：后端必须将本地时间 `startLocal` 与 `endLocal` 准确转换为对应的 UTC 时间（`startUtc` 和 `endUtc`）。
  - **Firestore 查询条件**：
    - 仅从当前登录用户的子集合 `users/{uid}/life_events` 中拉取。
    - 过滤条件：`isDeleted == false` AND `occurredAt >= startUtc` AND `occurredAt < endUtc`。
- **“当天没有事件”的处理与返回策略**：
  - 在查询数据库获取该用户该时间窗口内的所有 `LifeEvent` 之后，**若检索出的事件数量为 0，后端将直接拦截，绝不调用大语言模型（LLM）**。
  - 后端将直接硬编码构建并持久化保存一份“空状态总结”文档并返回，避免模型被逼硬夸产生赛博鸡汤。
  - **空状态总结结构**：
    *   `summary`: "这一天还没有记录。"
    *   `highlights`: 空数组 `[]`
    *   `moodLabel`: "暂无记录"
    *   `moodScore`: `null` (或 5)
    *   `suggestions`: 空数组 `[]`
- **Response** (`200 OK`)：
  ```json
  {
    "success": true,
    "data": {
      "id": "2026-06-25",
      "summary": "这是今天的一日总结...",
      "highlights": ["完成了 15km 骑行"],
      "moodLabel": "愉快",
      "moodScore": 8,
      "suggestions": ["明天继续保持运动"]
    }
  }
  ```

### 2. 获取每日总结 (GET /api/daily-summaries/{date})
- **路径**：`GET /api/daily-summaries/{date}` （例如 `/api/daily-summaries/2026-06-25`）
- **功能**：获取当前登录用户已生成的特定日期总结。
- **后台处理**：
  - 在当前用户的子集合路径 `users/{uid}/daily_summaries/{date}` 下定位文档，若不存在返回 `404 Not Found`。
- **Response** (`200 OK`)：
  - 结构同生成接口的 `data` 部分。

---

> [!IMPORTANT]
> **关于定时任务（Scheduler）接口的说明：**
> 供 Cloud Scheduler 自动运行使用的批量接口（例如 `/internal/agent/jobs/daily-summary`）在 Phase 2 中**不进行实现**，该规划已被明确调整并移入 **Phase 2 Plus** 阶段。Phase 2 的 Daily Summary 仅支持当前登录用户的 `POST /api/daily-summaries/generate` 手动触发。
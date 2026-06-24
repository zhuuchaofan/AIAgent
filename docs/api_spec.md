# LifeOS - 阶段 1 核心 API 规范 (API Specifications)

> [!NOTE]
> 本文档定义了 LifeOS **第 1 周 (阶段 1：生活记录 MVP)** 的 3 个核心 API。
> 所有请求和响应均需在 Header 中携带 Firebase ID Token 进行多用户身份验证与数据隔离。

---

## 🔒 通用请求头 (Global Headers)

每个 API 请求都必须携带以下头部：

```http
Authorization: Bearer <Firebase_ID_Token>
Content-Type: application/json
```

后端中间件 `FirebaseAuthMiddleware` 会对 Token 进行签名和有效期校验。验证成功后，从中读取 `uid`，将其映射为后端的 `userId` 挂载至上下文，数据未验证通过则返回 `401 Unauthorized` 状态码。

---

## 🔌 核心接口规格 (API Endpoints)

### 1. 投递记录并结构化提取 (Ingest Life Event)
* **接口路径**：`POST /api/life/ingest`
* **功能描述**：接收用户的原始生活随笔，通过 LLM 自动进行“结构化分类与抽取”，并将干净的强 schema 记忆入库。
  * ⚠️ **阶段 1 边界**：仅做单条文本结构化提取，不依赖多轮上下文。阶段 1 可检测明显指代，但不保证消解准确。多轮指代消解放入 P1 或第 2 周。
* **请求体 (Request Body)**：
  ```json
  {
    "text": "今天骑车18km，平均心率145，感觉大腿很酸。明天提醒我休息下。",
    "clientTimeZone": "Asia/Tokyo"
  }
  ```

> [!NOTE]
> **时区消解规则**：
> 1. 优先使用客户端请求传来的 `clientTimeZone`（IANA格式，如 `"Asia/Tokyo"`）。
> 2. 若客户端缺省，则尝试读取该用户的 Profile 配置。
> 3. 若仍缺失，则系统降级默认采用 `"Asia/Tokyo"`。
> 4. `occurredAt` 统一在服务器端被计算并转换为 **UTC 零时区** 保存，防止不同时区设备间的时间线穿插混乱。**阶段 1 约束**：`occurredAt` 默认等于记录创建时间；暂不解析“昨天、上周、上个月”等自然语言时间。自然语言时间解析放入后续增强，避免阶段 1 引入复杂的自然时间解析逻辑。

* **响应体 (Response Body - 200 OK)**：
  ```json
  {
    "success": true,
    "message": "已成功记录骑行事件。检测到提醒意图，但阶段 1 暂不支持提醒自动创建，该功能将在后续阶段开启。",
    "detectedReminderIntent": true,
    "reminderCreated": false,
    "data": {
      "id": "evt_20260624_cyc01",
      "type": "cycling",
      "schemaVersion": "v1",
      "title": "骑行 18km (疲劳度: medium)",
      "content": "今天骑车18km，平均心率145，感觉大腿很酸。明天提醒我休息下。",
      "occurredAt": "2026-06-24T05:46:47Z",
      "timeZone": "Asia/Tokyo",
      "tags": ["骑行", "健身", "大腿酸"],
      "importance": 2,
      "source": "manual",
      "structuredData": {
        "distanceKm": 18.0,
        "avgHeartRate": 145,
        "fatigue": "medium",
        "note": "感觉大腿很酸"
      },
      "extractionConfidence": 0.95,
      "needsReview": false,
      "createdAt": "2026-06-24T05:46:49Z"
    }
  }
  ```

* **常见错误响应**：
  * **400 Bad Request**：用户输入的 text 为空。
  * **401 Unauthorized**：未传 Token 或 Token 过期。
  * **422 Unprocessable Entity**：LLM 解析 JSON 错误或提取结果不匹配系统校验 schema。

---

### 2. 获取生活事件时间线流 (Get Events Timeline)
* **接口路径**：`GET /api/life/events`
* **功能描述**：采用 Cursor 分页方式，按时间线倒序（`occurredAt DESC`）获取当前认证用户记录的生活事实列表。
* **请求 Query 参数**：
  | 参数名 | 类型 | 必须 | 默认值 | 说明 |
  | :--- | :--- | :--- | :--- | :--- |
  | `type` | String | 否 | `all` | 过滤特定类型：`cycling` \| `home` \| `cat` \| `life` \| `unknown` |
  | `limit` | Integer| 否 | `20` | 单次返回最大数量（上限为 100） |
  | `cursor`| String | 否 | 无 | 上次请求返回的 Base64 编码游标 |

> [!NOTE]
> **游标设计规则**：`cursor` 通过 Base64 编码封装：`Base64("occurredAt|documentId")`（例如 `MjAyNi0wNi0yNFQwNTo0Njo0N1p8ZXZ0XzEyMw==`）。
> 后端在拉取数据时，解码得到上次最后一条事件的时间和 ID，并在查询中使用 `startAfter` 条件进行流式翻页，避免了 offset 的性能开销及边界漏单。
> 
> **Firestore 翻页双排序 C# 示例**：
> ```csharp
> query
>   .OrderByDescending("occurredAt")
>   .OrderByDescending(FieldPath.DocumentId)
>   .StartAfter(lastOccurredAt, lastDocumentId)
>   .Limit(limit);
> ```

* **响应体 (Response Body - 200 OK)**：
  ```json
  {
    "success": true,
    "nextCursor": "MjAyNi0wNi0yNFQwNTo0Njo0N1p8ZXZ0XzIwMjYwNjI0X2N5YzAx",
    "data": [
      {
        "id": "evt_20260624_cyc01",
        "type": "cycling",
        "schemaVersion": "v1",
        "title": "骑行 18km (疲劳度: medium)",
        "content": "今天骑车18km，平均心率145，感觉大腿很酸。明天提醒我休息下。",
        "occurredAt": "2026-06-24T05:46:47Z",
        "timeZone": "Asia/Tokyo",
        "tags": ["骑行", "健身"],
        "importance": 2,
        "source": "manual",
        "structuredData": {
          "distanceKm": 18.0,
          "avgHeartRate": 145,
          "fatigue": "medium"
        },
        "extractionConfidence": 0.95,
        "needsReview": false,
        "createdAt": "2026-06-24T05:46:49Z"
      }
    ]
  }
  ```

---

### 3. 查看特定事件详情 (Get Event Details)
* **接口路径**：`GET /api/life/events/{id}`
* **功能描述**：根据事件 ID 获取详细信息。
* **Path 参数**：
  * `id` (String)：生活事件 Document ID

> [!IMPORTANT]
> **安全防护（路径隔离）**：后端实现读取时，**严禁**直接在全局做未隔离查询。
> 必须优先通过 `users/{currentUserId}/life_events/{eventId}` 路径精确定位文档，由路径天然实现防止跨用户越权读取。只有在后续 collection group 全局跨集合查询时，才需要从文档属性中提取 `userId` 进行二次硬校验。

> [!NOTE]
> **开发调试字段 `rawLlmOutput` 限制**：
> `rawLlmOutput` 属于底层诊断用字段。
> - **入库与保存**：可正常入库，但仅用于开发调试与审计。
> - **生产环境拦截**：在 Production 生产环境下，所有 API 响应均不返回该字段（省去该字段或返回 `null`）。
> - **Timeline 列表**：`GET /api/life/events` 列表响应永远不返回该字段。
> - **Detail 详情**：`GET /api/life/events/{id}` 仅在开发/测试(Debug)环境下返回该字段。

* **响应体 (Response Body - 200 OK)**：
  > [!NOTE]
  > 以下示例为 Debug 环境响应；Production 环境下不返回 `rawLlmOutput`。

  ```json
  {
    "success": true,
    "data": {
      "id": "evt_20260624_cyc01",
      "type": "cycling",
      "schemaVersion": "v1",
      "title": "骑行 18km (疲劳度: medium)",
      "content": "今天骑车18km，平均心率145，感觉大腿很酸。明天提醒我休息下。",
      "occurredAt": "2026-06-24T05:46:47Z",
      "timeZone": "Asia/Tokyo",
      "tags": ["骑行", "健身"],
      "importance": 2,
      "source": "manual",
      "structuredData": {
        "distanceKm": 18.0,
        "avgHeartRate": 145,
        "fatigue": "medium"
      },
      "extractionConfidence": 0.95,
      "needsReview": false,
      "rawLlmOutput": "...",
      "createdAt": "2026-06-24T05:46:49Z"
    }
  }
  ```

---

## 🚨 统一错误响应规范 (Error Standards)

在系统运行出错时（无论是鉴权失败、大模型解析异常、数据格式校验未通过还是服务器内部崩溃），API 统一返回非 2xx 状态码及以下标准化 JSON 结构：

```json
{
  "success": false,
  "error": {
    "code": "INVALID_INPUT",
    "message": "text 不能为空",
    "details": {
      "fields": ["text"]
    }
  }
}
```

### 🏷️ 核心业务错误码一览 (Error Codes)

| 错误码 (`code`) | 关联 HTTP 状态码 | 业务场景描述 |
| :--- | :--- | :--- |
| `UNAUTHORIZED` | `401 Unauthorized` | 缺失 Token、Token 验签失败或 Token 已过期 |
| `INVALID_INPUT` | `400 Bad Request` | 接口入参为空、非 JSON 格式或不符合参数类型约束 |
| `LLM_PARSE_FAILED` | `422 Unprocessable`| LLM 没有输出合法的 JSON 格式数据 |
| `SCHEMA_VALIDATION_FAILED`| `422 Unprocessable`| 提取的数据无法被反序列化为后端强类型的 Record/Class |
| `EVENT_NOT_FOUND` | `404 Not Found` | 指定的事件 ID 不存在，或该数据属于其他用户（越权探测保护） |
| `INTERNAL_ERROR` | `500 Internal Server`| 数据库连接故障、云服务中断等非业务类服务器崩溃 |

# LifeOS - Firestore 数据结构设计 (Firestore Schema Spec)

> [!NOTE]
> 本文档定义了 LifeOS 在 Google Cloud Firestore 中的 Collection/Document 详细数据结构。
> 所有数据均按 `users/{userId}` 进行根级隔离，确保多用户数据完全隔离与安全。

---

## 🗺️ 集合层次结构总览 (Hierarchy)

```text
users/{userId} (根用户文档)
  ├─ profile (Map / 用户属性)
  │
  ├─ life_events/{eventId} (长期记忆事件集合)
  │
  ├─ reminders/{reminderId} (个人提醒集合)
  │
  ├─ chat_sessions/{sessionId} (聊天会话集合 - 工作内存)
  │    └─ messages/{messageId} (会话内的消息流)
  │
  ├─ cats/{catId} (猫咪主档案集合)
  │
  └─ agent_runs/{runId} (Agent 执行状态与工具调用历史)
```

---

## 📄 各集合详细字段定义 (Collection Specs)

### 1. 根用户文档 (Users)
* **路径**：`users/{userId}`
* **字段**：
  | 字段名 | 类型 | 说明 | 示例 |
  | :--- | :--- | :--- | :--- |
  | `createdAt` | Timestamp | 账号创建时间 | `2026-06-24T13:46:47Z` |
  | `profile` | Map | 用户基础信息 | `{ "displayName": "加一" }` |

---

### 2. 长期生活事件 (Life Events)
* **路径**：`users/{userId}/life_events/{eventId}`
* **字段**：
  | 字段名 | 类型 | 必须 | 说明 | 示例 / 可选值 |
  | :--- | :--- | :--- | :--- | :--- |
  | `id` | String | 是 | 事件唯一标识 | `evt_8932jfdsa9f` |
  | `userId` | String | 是 | 所属用户 UID，后端生成，不接受 LLM 或前端传入 | `test_user_01` |
  | `type` | String | 是 | 事件核心分类 | `cycling`, `home`, `cat`, `life`, `unknown` |
  | `schemaVersion`| String | 是 | 结构化数据模式版本 | `"v1"` (用于字段向后兼容) |
  | `title` | String | 是 | 系统/大模型生成的简短标题 | `"骑行 18km"` |
  | `content` | String | 是 | 用户输入的原始记录内容 | `"今天骑车 18km，感觉腿有点累。"` |
  | `occurredAt` | Timestamp | 是 | 事件实际发生时间 (统一为 UTC Z) | `2026-06-24T05:46:47Z` |
  | `timeZone` | String | 是 | 用户录入时的本地时区 | `"Asia/Tokyo"` |
  | `tags` | Array | 否 | 检索标签 | `["骑行", "健康", "运动"]` |
  | `importance` | Integer | 是 | 情感/生活重要程度 (1-5) | `3` (3及以上为总结时捞取的高光事件) |
  | `source` | String | 是 | 记录来源 | `manual` (手动录入), `agent` (Agent 触发) |
  | `extractionConfidence` | Double | 是 | LLM 提取的置信度 (0.0 - 1.0) | `0.95` |
  | `needsReview` | Boolean | 是 | 是否需要人工确认标志 | `false` |
  | `rawLlmOutput` | String | 否 | 调试用：原始大模型输出的 JSON 文本 (生产环境可选保存) | `"{\n  \"type\": \"cycling\", ...}"` |
  | `createdAt` | Timestamp | 是 | 数据入库时间 (统一为 UTC Z) | `2026-06-24T05:46:49Z` |
  | `structuredData`| Map | 是 | 与 `type` 强绑定的动态结构化数据 | （详见下方定义） |

> [!IMPORTANT]
> **结构化数据缺省填充规则**：
> 如果大语言模型未能成功从文本中提取出某个字段（例如骑行中的 `durationMinutes`），后端写入 Firestore 时**必须完全省略该字段（不写入/Key缺省）**。
> **严禁硬编码默认值（如 `0`）填充**，避免“0分钟”、“0元”等脏数据污染后续的数据分析和均值统计。

#### 📁 `structuredData` 不同 `type` 的 JSON 格式约束 (v1)

##### 🚴‍♂️ 骑行 `cycling`
```jsonc
{
  "distanceKm": 18.0,
  "durationMinutes": 55, // 可选，若未提取则不写入，不可写 0
  "avgHeartRate": 145,   // 可选，若未提取则不写入
  "fatigue": "medium",   // low | medium | high
  "note": "感觉腿有点累"
}
```

##### 🐱 猫咪 `cat`
```jsonc
{
  "catName": "黑猫",
  "symptom": "呕吐",     // 可选，若未提取则不写入
  "count": 1,            // 可选，呕吐次数（若提取到则写入，否则省略）
  "mentalState": "正常",  // 可选，精神状态（若提取到则写入，否则省略）
  "action": "打疫苗",     // 可选，预防性动作
  "cost": 300.0,         // 可选，消费金额（只有明确消费时才写入，不可写入默认值 0.0）
  "weight": 4.5          // 可选，最新体重(kg)
}
```

##### 🏠 家庭事项 `home`
```jsonc
{
  "item": "客厅空调",
  "action": "清理滤网",   // 清理 | 维修 | 缴费
  "cost": 150.0,         // 可选，只有明确提到花费金额（或明确没花钱：0.0）时才写入，否则省略该键，绝不能默认填充 0.0
  "nextSuggestion": "30天后再次检查"
}
```

##### 🌟 人生记录 `life`
```jsonc
{
  "category": "anniversary", // memory (回忆) | anniversary (纪念日) | milestone (里程碑)
  "emotion": "happy",        // positive | happy | neutral | low
  "keyFigures": ["加一"]
}
```

##### ❓ 未知/无法匹配类型 `unknown`
当用户输入的随笔内容无法匹配到 `cycling`、`cat`、`home`、`life` 中的任何具体强 Schema 时，系统自动回退分类为 `unknown`。
```jsonc
{} // 允许为空 Map
```
* **特殊校验与风控规则**：
  * 当 `type = "unknown"` 时，`structuredData` 必须允许为空 Map `{}`。
  * 其余系统字段（如 `id`、`userId`、`title`、`content`、`occurredAt`、`timeZone`、`importance`、`extractionConfidence`、`needsReview` 等）仍必须完整存在并经过强验证。
  * **额外风控降级**：若大语言模型的提取置信度偏低（`extractionConfidence < 0.7`），则一律强制置 `needsReview = true`，交由用户进行后续的人工确认审核。

---

### 3. 个人提醒 (Reminders)
* **路径**：`users/{userId}/reminders/{reminderId}`
* **字段**：
  | 字段名 | 类型 | 说明 | 示例 / 可选值 |
  | :--- | :--- | :--- | :--- |
  | `id` | String | 唯一标识 | `rem_302jf89df90` |
  | `title` | String | 提醒标题 | `"清理客厅空调滤网"` |
  | `category` | String | 关联事件类型 | `home`, `cat`, `cycling`, `general` |
  | `dueAt` | Timestamp | 提醒到期时间 | `2026-07-24T09:00:00Z` |
  | `status` | String | 提醒状态 | `pending` (待办), `completed` (已完成), `missed` (过期) |
  | `repeatRule` | String | 重复规则 | `none`, `daily`, `weekly`, `monthly` |
  | `createdAt` | Timestamp | 创建时间 | `2026-06-24T13:46:47Z` |

---

### 4. 聊天会话与消息流 (Chat Sessions)
* **路径**：`users/{userId}/chat_sessions/{sessionId}`
* **字段**：
  | 字段名 | 类型 | 说明 | 示例 |
  | :--- | :--- | :--- | :--- |
  | `id` | String | 会话唯一标识 | `ses_78f89dsf` |
  | `title` | String | 会话标题（首句大模型总结） | `"关于黑猫呕吐的记录咨询"` |
  | `lastMessageAt`| Timestamp | 最新一条消息时间 | `2026-06-24T13:50:00Z` |
  | `createdAt` | Timestamp | 会话发起时间 | `2026-06-24T13:46:47Z` |

* **消息子集合路径**：`users/{userId}/chat_sessions/{sessionId}/messages/{messageId}`
* **字段**：
  | 字段名 | 类型 | 说明 | 示例 / 可选值 |
  | :--- | :--- | :--- | :--- |
  | `id` | String | 消息唯一标识 | `msg_89dafsdf` |
  | `role` | String | 消息发送方 | `user` (用户), `assistant` (智能体) |
  | `content` | String | 消息内容 | `"今天黑猫吐了，精神还可以。"` |
  | `createdAt` | Timestamp | 发送时间 | `2026-06-24T13:46:48Z` |

---

### 5. 猫咪档案 (Cats)
* **路径**：`users/{userId}/cats/{catId}`
* **字段**：
  | 字段名 | 类型 | 说明 | 示例 |
  | :--- | :--- | :--- | :--- |
  | `id` | String | 猫咪 ID | `cat_black` |
  | `name` | String | 昵称 | `"黑猫"` |
  | `gender` | String | 性别 | `male` |
  | `birthDate` | Timestamp | 生日 | `2023-04-12T00:00:00Z` |
  | `weight` | Double | 最新体重(kg) | `4.5` |
  | `createdAt` | Timestamp | 档案创建时间 | `2026-06-24T13:46:47Z` |

---

### 6. Agent 运行日志 (Agent Runs)
* **路径**：`users/{userId}/agent_runs/{runId}`
* **字段**：
  | 字段名 | 类型 | 说明 | 示例 / 可选值 |
  | :--- | :--- | :--- | :--- |
  | `id` | String | 运行记录 ID | `run_9083fjsda` |
  | `trigger` | String | 触发源 | `user_request` (用户请求), `cron_daily` (每日定时任务) |
  | `status` | String | 执行状态 | `running`, `completed`, `failed` |
  | `toolCalls` | Array | 记录执行过哪些工具调用过程 | `[{"tool": "save_life_event", "args": {...}}]` |
  | `result` | String | 运行最终结果/总结回复 | `"已成功保存该骑行事件，并为您创建了明天的观察提醒。"` |
  | `startedAt` | Timestamp | 开始时间 | `2026-06-24T13:46:47Z` |
  | `endedAt` | Timestamp | 结束时间 | `2026-06-24T13:46:50Z` |

---

## 🎯 索引配置建议 (Indexing Strategy)

在 Firestore 中，用户隔离天然由子集合路径（`users/{userId}/...`）隐式支持。为了保障时间线的高效拉取与过滤，我们需要分两种场景建立单字段索引与复合索引：

### 1. 普通用户查询（Subcollection Queries）
* **场景描述**：用户在客户端上直接拉取**自己**的 `life_events` 列表（例如：过滤我的骑行事件，按时间倒序）。
* **路径**：直接对 `users/{userId}/life_events` 进行单集合查询。
* **复合索引要求**：**不依赖 `userId` 字段索引**，因为 Uid 已经在路径里隐式隔离。
  * **集合：`life_events`**
    * **无 type 过滤时**：
      * `occurredAt` DESC ➡️ `__name__` DESC
    * **有 type 过滤时**：
      * `type` ASC ➡️ `occurredAt` DESC ➡️ `__name__` DESC
  * **集合：`reminders`**
    * 索引字段：`status` ASC ➡️ `dueAt` ASC

### 2. 后台统计 / 跨用户汇聚（Collection Group Queries）
* **场景描述**：管理员对全系统所有用户的生命周期事件进行全局趋势统计，或者做 Collection Group 查询。
* **路径**：跨越父文档，全局扫描所有名为 `life_events` 的子集合。
* **复合索引要求**：此时必须在 Document 内部冗余 `userId` 字段，并建立如下 Collection Group 复合索引：
  * **集合组：`life_events`**
    * 索引字段：`userId` ASC ➡️ `type` ASC ➡️ `occurredAt` DESC
  * **集合组：`reminders`**
    * 索引字段：`userId` ASC ➡️ `status` ASC ➡️ `dueAt` ASC

# Phase 2 LLM Prompt Contract (JSON 约束契约)

## 一、 Ingest 阶段 LLM 输出约束更新 (支持 Reminder)

为了在一次用户输入中同时提取 `LifeEvent` 和 `Reminder`，我们需要调整 Ingest 阶段 the System Prompt，使其约束输出严格合法的 JSON 格式。

### 1. JSON Contract (输出结构)
LLM 的输出必须严格遵守以下 JSON 结构（**严禁包含 ```json 标记，且保证为无任何注释的合法 JSON**）：

```json
{
  "type": "string",
  "title": "string",
  "content": "string",
  "tags": ["string"],
  "importance": 1,
  "structuredData": {},
  "extractionConfidence": 0.85,
  "needsReview": false,
  "reminder": {
    "hasIntent": false,
    "title": "string",
    "description": null,
    "dueAtIso8601": null,
    "parseStatus": "none"
  }
}
```

### 2. 字段定义说明
*   `reminder.hasIntent` (Boolean)：是否成功识别到用户的提醒意图。
*   `reminder.title` (String/Null)：提醒的精简标题。若 `hasIntent` 为 false，则输出 null。
*   `reminder.description` (String/Null)：提醒的详细描述。若无则输出 null。
*   `reminder.dueAtIso8601` (String/Null)：提醒过期的 UTC ISO-8601 时间字符串。如果时间完全缺失或无法推断，**必须输出为 `null`**。
*   `reminder.parseStatus` (String)：提醒解析状态。

### 3. Reminder ParseStatus 枚举定义与应用层行为
为避免污染 JSON Contract，该枚举值不在 JSON 中通过注释描述，其允许的取值范围如下：
*   `none`: 未检测到任何提醒意图（`hasIntent` 为 false 时的默认值）。
*   `success`: 成功检测到意图且能够解析出具体的时间边界（`dueAtIso8601` 必须为有效的 UTC 时间字符串，符合 ISO 8601 标准）。**只有此状态下，后端应用层才会向 `users/{uid}/reminders` 写入物理提醒文档，且写入时由后端自动补齐 `repeatRule: "none"` 默认属性**。
*   `missing_due_time`: 检测到意图，但用户指令中完全缺失时间（例如“以后记得提醒我”），此时 `dueAtIso8601` 必须为 `null`。**此时应用层只创建 LifeEvent，绝不创建物理 Reminder 文档**。
*   `invalid_due_time`: 检测到意图，但提供的时间描述不合法或无法换算（例如“在公元前提醒我”）。**此时应用层只创建 LifeEvent，绝不创建物理 Reminder 文档**。
*   `llm_parse_failed`: LLM 在内部尝试换算时间或组织意图时自我判定失败。**此时应用层只创建 LifeEvent，绝不创建物理 Reminder 文档**。

### 4. 时间解析与边界规则
Prompt 中必须明确告知 LLM 当前的 UTC 时间和用户的本地时区（例如 `Asia/Shanghai`），要求：
*   **强制转换**：LLM 计算出的提醒时间 `dueAtIso8601` 必须转换为 **UTC 时间**的 ISO 8601 格式输出。
*   **缺省时间补全**：
    *   If 用户说“明天提醒我喝水”未指定具体时刻，默认补全为当地时间 `09:00`，然后再转为 UTC 输出。
    *   If 用户说“晚上提醒我”，默认补全为当地时间 `20:00`，然后再转为 UTC 输出。
*   **时间完全缺失**：
    *   If 用户说“以后提醒我买药”或“记得提醒我”但完全无法推断具体日期，LLM 应设置 `"parseStatus": "missing_due_time"`，此时 `dueAtIso8601` 输出为 `null`。
    *   后端发现 `missing_due_time` 时，**仅创建 LifeEvent，不创建 Reminder 物理实体落库**，并在 Event 实体内记录 `reminderParseStatus`。

---

## 二、 Daily Summary 阶段 LLM 输出约束

### 1. 任务说明
Daily Summary 由当前登录用户手动调用 API 触发。后端提取通过 `targetDate + clientTimeZone` 过滤的本地自然日范围内的所有 `LifeEvent` 数据。由于需要将这些数据传递给 LLM 组装，LLM 必须保证输出一个结构严密且合法的 JSON。

### 2. JSON Contract (输出结构)
输出格式必须为**不带任何代码注释的标准 JSON**：

```json
{
  "summary": "一段约50-100字的总结，用第一人称或温馨的第三人称视角，概括这一天发生的核心事件。",
  "highlights": [
    "完成了 15km 骑行，虽然很累但坚持了下来",
    "发现黑猫呕吐，并及时记录观察"
  ],
  "moodLabel": "疲惫但充实",
  "moodScore": 7,
  "suggestions": [
    "建议明天早点休息，恢复体力",
    "明天注意观察猫咪精神状态"
  ]
}
```

### 3. 字段定义说明
*   `summary` (String)：一日的总结概览文本。
*   `highlights` (Array of Strings)：今日最亮眼或最具有代表性的事件提炼列表。
*   `moodLabel` (String)：情绪的感知和提炼标签（如“平静”、“轻微焦虑”等）。
*   `moodScore` (Integer)：1 至 10 之间的整数，10 分为最高（最开心/最健康），1 分为最低。
*   `suggestions` (Array of Strings)：大模型根据今日发生的事项，智能生成的面向次日或未来的改进与关怀建议。

### 4. Fallback 策略与容错
*   **LLM 解析失败（JSON格式错误）**：在 Daily Summary 场景下，由于是异步/后台任务，失败时不会阻塞用户主流程。后台代码捕获 JsonException，记录 `status="failed"`, `errorMsg`（保存简要脱敏后的信息）落入 `agent_runs` 集合，结束流程。不向前端抛异常。
*   **输出自然语言包裹**：代码必须能够剔除首尾的 Markdown 标记（如存在），然后再尝试反序列化。如果仍失败，同上记录到执行日志。
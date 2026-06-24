# LifeOS - 阶段 1 核心开发任务清单 (Phase 1 MVP Tasks)

> [!IMPORTANT]
> **本周核心目标**：实现“一句话记录生活”的核心业务闭环。
> 严防项目过载。优先完成 **P0（核心阻塞项）**，打通 “输入 ➡️ 模拟/解析 ➡️ 校验 ➡️ 存储” 链路；**P1（增强优化项）** 可在链路跑通后逐步收尾。
> **开发准则**：先 Mock 大模型调通数据库和鉴权，最后再接入真实 LLM。

---

## 🔥 P0 任务清单 (本周必须完成)

### 1. 后端核心研发 (Backend P0)
- [ ] **1.1. 初始化 `LifeAgent.Api` 项目**
  - 使用 .NET 8.0 Web API 模板初始化项目：`dotnet new webapi -n LifeAgent.Api`
  - 清除 WeatherForecast 等无用模板文件。
- [ ] **1.2. 接入 Firebase Admin SDK 基础配置**
  - 引入 NuGet 包：`FirebaseAdmin`
  - 配置 Firebase 引导代码，支持从环境变量或 Local Emulator 加载测试凭证。
- [ ] **1.3. 实现 `FirebaseAuthMiddleware` 多用户隔离**
  - 编写自定义 HTTP 中间件，拦截 header 中的 `Authorization: Bearer <token>` 并调用 SDK 验签。
  - 将验签解析出的 `userId` (UID) 注入 HttpContext 的 Items 中作为多用户隔离的主键。
- [ ] **1.4. 建立强类型的 `LifeEvent` 模型**
  - 编写 `LifeEvent` 实体类，包含 `id`、`userId`、`type`、`schemaVersion = "v1"`、`occurredAt (UTC)`、`timeZone`、`tags`、`importance`、`source = "manual"`、`structuredData` (Dictionary)。
  - 增加字段：`extractionConfidence`、`needsReview`、`rawLlmOutput`。
- [ ] **1.5. 实现 `LifeEventService`（先不接大模型，测试手写假数据）**
  - 引入 NuGet 库：`Google.Cloud.Firestore`
  - 编写 `SaveEventAsync` 写入 Firestore，以及 `ListEventsAsync`（支持按 `occurredAt DESC` 且 `FieldPath.DocumentId DESC` 双字段排序查询）。
  - 编写 `GetEventByIdAsync` 通过精确的用户路径安全读取单个事件。
- [ ] **1.6. 实现 `POST /api/life/ingest`（Mock LLM 解析阶段）**
  - 接收用户原始文本及 `clientTimeZone`。不调用大语言模型，**手写 Mock 解析器**（如：若包含“骑行”二字，直接 Mock 输出 Cycling 类型数据）。
  - 将结构化结果保存至 Firestore，并返回带有 Mock 调试标志的 JSON。
- [ ] **1.7. 实现 `GET /api/life/events`（基于 Cursor 游标分页）**
  - 接收 `limit` 和 `cursor` 参数（Base64 封装的 `"occurredAt|documentId"`）。
  - 按照 `occurredAt DESC` 和 `FieldPath.DocumentId DESC` 顺序进行 Firestore 游标翻页（`startAfter`），返回带 `nextCursor` 的标准响应。
- [ ] **1.8. 实现 `GET /api/life/events/{id}`（路径隔离读取单个事件详情）**
  - 必须通过 `users/{currentUserId}/life_events/{eventId}` 路径精确定位并读取文档，从架构级防止越权探测。
  - 不允许 collection group 或全局未隔离查询。
  - 若文档不存在或不属于当前用户，统一拦截并返回 `EVENT_NOT_FOUND` (404) 错误码。
- [ ] **1.9. 编写全局异常拦截与标准错误响应**
  - 编写全局 `ExceptionMiddleware` 异常处理器，拦截所有崩溃并统一输出 `{ "success": false, "error": { "code": "...", "message": "..." } }` 格式。
- [ ] **1.10. 实现最小 Schema 校验器 (强类型 JSON 结构校验)**
  - 编写 C# Record/Class，在数据落库前对不同 `type` 的 `structuredData` 进行强类型反序列化校验，防止模型返回非标准字段或漂移。该模块必须在接入真实 LLM 之前实现。
- [ ] **1.11. 接入真实 `LlmService`（大模型提取集成）**
  - 封装大语言模型接口调用（调用 Gemini API），在 Mock 链路及 Schema 校验完全验证无误后，替换 Ingest 接口中的 Mock 解析器为真实模型提取逻辑。

### 2. 前端核心研发 (Frontend P0)
- [ ] **2.1. 极简登录态接入**
  - 引入 Firebase Auth JS SDK，实现最基础的登录，并在 LocalStorage 缓存 `idToken`。
- [ ] **2.2. 极简一句话记录输入框**
  - 一个 Textarea 和提交按钮，点击后将 Token 带入 Headers，请求 `POST /api/life/ingest`。
- [ ] **2.3. 时间线记录列表页**
  - 调用 `GET /api/life/events` 拉取事件，列表渲染显示核心指标、类型和原始文本。
  - 底部提供“加载更多”按钮，带上 `nextCursor` 实现流畅翻页。

---

## ✨ P1 任务清单 (可后续逐步完善)

- [ ] **1. Ingest 多轮指代消解上下文** (Backend)
  - 在 Ingest 中，传入该会话最近 24 小时的生活记录，使 LLM 能将“它精神还好”中的“它”正确指代消解。
- [ ] **2. 前端：事件详情对话框** (Frontend)
  - 点击列表中的单项，弹窗展示该事件的完整结构化 JSON 与 `needsReview` 状态。
- [ ] **3. Cloud Run 手动部署与 Secret Manager 配置** (Cloud)
  - 编写后端 `Dockerfile`。
  - 在 Google Cloud Platform 启用 Firestore，在 GCP Secret Manager 中安全创建并配置 LLM API Key，挂载为容器环境变量。
  - 手动将服务部署至 Cloud Run 并进行线上验收。
- [ ] **4. 自动化 CI/CD 部署** (P2 / 后续)
  - 配置自动化部署流水线（如 GitHub Actions 或 GCP Cloud Build），实现代码合并后自动构建与部署。

---

## 🎯 阶段 1 最小验收标准 (Acceptance Criteria)

1. **POST Ingestion 验证**：
   发送 `POST /api/life/ingest`，Body 携带 `{"text": "今天骑车18km，平均心率145，大腿感觉有些酸。明天提醒我休息下。", "clientTimeZone": "Asia/Tokyo"}`。
   * **期望返回**：`success: true`，`message` 明确提示：*"已成功记录骑行事件。检测到提醒意图，但阶段 1 暂不支持提醒自动创建，该功能将在后续阶段开启。"*
   * **Firestore 数据验证**：
     * `type` 自动为 `"cycling"`
     * `timeZone` 捕获到如 `"Asia/Tokyo"`
     * `occurredAt` 统一为 UTC 时间
     * `needsReview` 为 `false`
     * `structuredData` 解析出：`{"distanceKm": 18.0, "avgHeartRate": 145, "fatigue": "medium"}`
2. **GET Timeline 游标翻页验证**：
   请求 `GET /api/life/events?limit=1`。
   * **期望返回**：返回带有上述骑行事件的数组，以及一个 Base64 编码的 `nextCursor`（如 `"MjAyNi0wNi0yNFQwNTo0Njo0N1p8ZXZ0XzIwMjYwNjI0X2N5YzAx"`）。
   * 带上 `cursor` 请求第二页，验证数据返回为空且没有出现报错，状态码返回 `200 OK`。


# LifeOS - 阶段 1 开发执行顺序 (Phase 1 Execution Order)

> [!NOTE]
> 本文档定义了 **第 1 周 (阶段 1：生活记录 MVP)** 的具体开发执行顺序（即“菜谱”），指导我们按正确的工程节奏递进编码，避免同时联调多项技术栈。

---

## 🍳 核心编码菜谱 (14-Step Execution Recipe)

为了获得最平滑的开发体验，我们强烈建议按照以下 **14 个严格步骤** 顺序依次编码、本地调试和提交。

### 🧱 [第一步：地基搭建] 本地基础设施初始化

- [ ] **1. 初始化 .NET API 项目**
  * 在命令行中创建骨架项目：`dotnet new webapi -n LifeAgent.Api`
  * 清除默认的 WeatherForecast 相关的样例代码。
- [ ] **2. 本地跑通 Health Check**
  * 新增一个极简的 Health 接口，确保在本地启动运行后，请求 `GET /health` 能返回 `200 OK` 且内容为 `"healthy"`，确保 Web 服务器处于就绪状态。

### 🔐 [第二步：安全拦截] Firebase 鉴权拦截器

- [ ] **3. 引入 Firebase 依赖并创建拦截器 (MOCK AUTH 优先)**
  * 引入 NuGet 包：`FirebaseAdmin`。
  * 编写 `FirebaseAuthMiddleware.cs`。
  * **💡 本地提速技巧**：在中间件中支持读取环境变量（如 `USE_MOCK_AUTH=true`）。若是本地开发，则直接硬编码注入一个测试用户 `userId = "test_user_01"`，从而**跳过真实的 Firebase 联调认证拦截**，加速开发。

### 💾 [第三步：存储底座] Firestore 事件持久化

- [ ] **4. 建立强类型数据模型与 Firestore 初始化**
  * 编写 `LifeEvent.cs` 实体类，包含 `UserId`、`RawLlmOutput`（设为可空 `string?`）、`StructuredData` 等。
- [ ] **5. 实现 LifeEventService.SaveEventAsync**
  * 引入 NuGet 包：`Google.Cloud.Firestore`。
  * 实现 `SaveEventAsync` 写入 Firestore 对应的 `users/{userId}/life_events/{eventId}`。
  * **💡 本地验证**：手写测试代码测试单条 Mock `LifeEvent` 能否 100% 成功保存到 Firestore。
- [ ] **6. 实现 LifeEventService.ListEventsAsync**
  * 实现基于 `occurredAt DESC` 且 `FieldPath.DocumentId DESC` 进行复合排序的列表拉取逻辑，为后续游标查询奠定底座。

### 🧬 [第四步：核心网关] 接口骨架与 Mock 联调

- [ ] **7. 编写 POST /api/life/ingest 接口 (Mock 解析)**
  * 接收用户原始文本和时区。**不要接入真实 Gemini 调用**。手写一个简单的 Mock 解析器（如果输入文本含“骑行”，直接 Mock 构造并填充 `cycling` 类型的 `StructuredData` 结果；含“猫”则构造 `cat` 结构）。
  * 调用 `LifeEventService.SaveEventAsync` 保存这条 Mock 数据。
- [ ] **8. 编写 GET /api/life/events 接口 (游标分页)**
  * 编写接口，基于 `limit` 和 `cursor`（Base64 编码的 `"occurredAt|documentId"`) 进行双排序游标分页。
  * 确认翻页逻辑与 `nextCursor` 完全正常，不出现数据重漏。
- [ ] **9. 编写 GET /api/life/events/{id} 接口 (路径安全读取)**
  * 编写接口，通过精确的路径 `users/{currentUserId}/life_events/{eventId}` 读取单条事件。
  * 找不到或越权访问统一返回 404 `EVENT_NOT_FOUND` 业务错误。
- [ ] **10. 编写 ExceptionMiddleware 全局异常拦截器**
  * 编写全局异常处理器，确保在参数错（400 INVALID_INPUT）、LLM/格式错（422）等情况下，能正确且统一地输出标准错误 JSON 结构。

### 🎨 [第五步：全栈打通] 前端 UI 与输入测试

- [ ] **11. 开发前端极简登录与输入/时间线面板**
  * 创建一个极简网页，完成 Firebase 登录并取得 `idToken`。
  * 制作输入框及时间线列表页（加载更多按钮携带 `nextCursor` 请求翻页）。
  * 在本地使用 Mock 接口测试整个全栈流程，确保登录、输入、保存、列表呈现、游标翻页无缝闭环。详情接口可通过 Postman / curl 验证；前端详情弹窗放入 P1。

### 🧠 [第六步：灵魂注入] 校验器与真实 Gemini 集成

- [ ] **12. 实现最小 Schema 校验器 (强类型反序列化)**
  * 编写强类型 Schema 验证代码。在数据落库前对不同 `type` 的 `structuredData` 进行强类型反序列化和约束校验（如 `unknown` 时允许空字典，其余字段均存在且满足风控置信度等）。
  * **绝对不允许硬编码默认值 0.0 入库**。
- [ ] **13. 接入真实 LlmService (Gemini API 联调)**
  * 封装并调用真实 Gemini API，替换 Ingest 接口中的 Mock 解析器为真实模型提取逻辑。
  * 编写解析 Prompts。要求 Gemini 仅提取纯业务 JSON，严禁其操纵系统元数据（`id`、`userId`、`createdAt` 等）。
  * 本地联调测试整个解析与入库流程，通过 Mock 账户进行功能性闭环测试。
- [ ] **14. Cloud Run 部署与密钥配置**
  * 编写后端 `Dockerfile`。
  * 在 GCP Secret Manager 中安全创建并配置 LLM API Key，挂载为容器环境变量。
  * 部署服务至 Cloud Run 并进行最终的生产线上验收。
# Phase 3 开发执行顺序与迭代规范 (Execution Order)

为了降低研发风险，确保每一段合并的代码都具有可验证性、稳定性和高正确度，Phase 3 的开发被严格拆分为以下 9 个递进步骤。每一步开发必须在验证通过（Green Light）之后，才能进入下一步。

---

## 阶段零：技术预研 (首要防线)

### 步骤 0：Firestore 向量最近邻技术 Spike (S1)
- **目标**：在没有业务和 UI 干扰的情况下，打通向量落库和相似度检索的最简路径，确认基础类库和云端 IAM 权限就绪。
- **执行动作**：
  1. 通过 gcloud CLI 在 `chunks` 集合的 `embedding` 字段创建 768d COSINE 向量索引。
  2. 编写一个最简的 Console 测试小程序或 API 隐藏 Endpoint，向特定的测试用户子集合写入两个 768 维 `VectorValue` 记录。
  3. 通过本地 HttpClient 或 curl 命令行，对 REST runQuery 检索接口进行实测验证。拟采用路径 `POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery`，但在未经 Spike 验证前**绝对不得将其表述为最终正确路径**。
  4. Spike 阶段必须实测对齐与验证：parent path 拼接格式、from collectionId=chunks 的匹配性、distanceResultField 字段定义、VectorValue 向量数值打包格式、以及后端返回 JSON parser 的正确反序列化，并核对 Cloud Run 的 IAM 权限。
- **可验证标准 (Green Light)**：
  - **Spike 成功率 100%**：控制台或 API 返回最近邻匹配的测试 chunks，且物理路径严格隔离，没有报错抛出。**若未通过该 Spike，绝对禁止开启接下来的核心向量开发。**
  - **Spike 结论（详见 [spike_report.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase3/spike/spike_report.md)）**：确认 SDK 4.3.0 不暴露出 `VectorValue` / `FindNearest` 类型，必须使用 REST API (commit 与 runQuery) 替代高级 SDK 向量接口。

---

## 阶段一：基础设施与底层存储 (底座搭建)

### 步骤 1：云端基础设施配置 (O1, O3)
- **目标**：在 Google Cloud 端配置好与 Cloud Run 区域高度一致的 Bucket 及 Cloud Tasks 队列。
- **执行动作**：
  1. 物理创建 GCS Bucket，区域必须与 Cloud Run 及 Firestore 所在的 Region 保持高度对齐，避免跨区带宽收费。
  2. 物理创建 Cloud Tasks 队列 `lifeos-ingestion-queue`。
  3. 授权 Cloud Run 服务账户。
- **可验证标准 (Green Light)**：
  - 在 GCP 控制台查看，Bucket 区域与 Tasks 队列状态正常，服务账户权限配置成功。

### 步骤 2：数据实体模型与底层存储库实现 (B1, B2)
- **目标**：搭建 .NET 的实体基础与底层 GCS 物理物理文件操作层。
- **执行动作**：
  1. 在 `LifeAgent.Api/Models` 创建 `Document` 与 `Chunk` 映射模型。
  2. 实现 GCS 封装工具类 `GoogleCloudStorageService.cs`，其内部物理文件存取强制使用 `/users/{uid}/...` 作为前缀目录。
- **可验证标准 (Green Light)**：
  - 针对 `GoogleCloudStorageService` 的单元测试通过，能成功在 GCS 写入、拉取和删除物理流。

---

## 阶段二：文档管理与抽取加工 (中台打通)

### 步骤 3：文档生命周期 API 与 Cloud Tasks 异步回调安全防护 (B3, B4)
- **目标**：打通安全的文件上传、列表 API 路由，配置 Google Cloud Tasks 后台触发，并建立极严格的 OIDC 鉴权与 Payload 零信任双重安全防线。
- **执行动作**：
  1. 实现 Minimal API Group `/api/v1/documents` 路由。
  2. 实现 `POST /api/v1/documents`：上传文件至 GCS 对应用户的多租户隔离目录，向 Firestore 初始化元数据，**向 Cloud Tasks 投递异步 Ingestion 任务**，投递成功后，接口立即返回 `202 Accepted`。
  3. 实现回调内部端点 `/internal/api/v1/documents/process`：
     - **安全防护 A（OIDC Token 三重拦截校验）**：后端中间件强制校验回调 Request Header 中的 `Authorization: Bearer <OIDC_Token>`。必须验证其 Google 签名、Issuer (必须为 `https://accounts.google.com`)、Audience (必须匹配当前实例的 Cloud Run 公网 URL)、以及绑定的专用 Service Account。`X-Appengine-QueueName` 只用作辅助审计日志。
     - **安全防护 B（Payload 零信任双重校验）**：后端**绝不盲信** Payload 传入的 `userId`、`documentId` 和 `gcsPath`。收到任务后，Worker 必须用 `documentId` 作为唯一主键从 Firestore 中重新读取文档真实的 metadata。对比确认元数据中的 `userId` 是否与 payload 一致；校验 `gcsPath` 前缀必须符合强多租户规范：`users/{userId}/documents/{documentId}/`；**只有 status 等于 "processing" 的文档才允许进入后续的文本解析处理**，防止已逻辑删除或重复消费的脏任务被执行。
- **可验证标准 (Green Light)**：
  - 发送 HTTP Mock 上传测试 PDF，接口立刻返回 202，元数据状态初始化为 `processing`。
  - Cloud Tasks 成功发起回调。不携带合法 OIDC Token 或携带伪造 OIDC Token 访问内部端点，后端拦截器必须精准抛出 `401 Unauthorized`。
  - 投递 Payload 含有非法 `gcsPath`、不匹配的 `userId`、或状态不为 `processing` 的任务，后端必须能正确识别并安全拒绝，打印审计安全警报。
  - 正常流程的 Tasks 在数秒内通过 OIDC 安全验证并执行空转消费（将状态安全更新为 `success`）。

### 步骤 4：文本抽取与语义段落切片 (B5)
- **目标**：实现 PDF 文本抽取及段落优先重叠合并的分块逻辑。
- **执行动作**：
  1. 在后台 process 回调中接入 `PdfPig`，抽离 Raw Text。
  2. 实现段落切割与 800 字符近似合并（重叠度 10%~15%）的 Chunker 算法，并将 pageNumber 等溯源数据绑定至 Chunk Metadata。
- **可验证标准 (Green Light)**：
  - 单元测试通过：输入一份含有复杂段落的 PDF 样例，成功抽取出全部文本并进行语义分块，产生合理数量 chunks，并满足 chunk 长度（目标 800 字符左右）、overlap 重叠度、以及 metadata 完整性要求，切片无乱码截断，前后过渡平滑。

---

## 阶段三：向量生成与智能会话 (核心攻坚)

### 步骤 5：向量化与 REST 检索实现 (B6, B7)
- **目标**：计算 Chunk 向量，并执行基于 REST 接口的最近邻检索与阈值拦截。
- **执行动作**：
  1. 异步调用 `gemini-embedding-001` 计算 768d 向量并落库（**显式设置 outputDimensionality = 768**）。物理保存时，**必须使用 REST commit 接口**打包为 `mapValue`（带 `__type__ = "__vector__"` 标记）格式物理提交。
  2. 实现向量检索：**必须基于 REST runQuery / findNearest 实现最近邻搜索**，并在 URL 中以 `users/{userId}` 锁定多租户范围。
  3. **自动化快照测试**：必须基于 [spike_report.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase3/spike/spike_report.md) 中实测返回的物理报文结构组织自动化快照测试 (Snapshot Testing) 保证 JSON Parser 解析高度正确。
  4. **实现相似度过滤配置化**：相似度门限不得作为常量写死。必须支持通过 `appsettings.json` 或环境变量获取配置（以余弦距离 `0.35` / 相似度得分 `0.65` 作为初始默认值）。检索时自动对超出配置距离门限的 Chunks 执行硬过滤拦截。
  5. **记录检索审计日志**：模块运行时，必须将 topK 各个分块的实际 `distance` / `score` 指标输出至详细日志，便于生产调优。
- **可验证标准 (Green Light)**：
  - 触发一次上传，解析落库后，Firestore 中该用户的 `chunks` 子集合中包含 embedding 字段（类型：原生 `VectorValue`）。
  - 运行检索，系统在日志中成功打印 topK 分块的 `distance` / `score`，且只有满足配置阈值的高相关 Chunks 能够被召回，低相关的噪声 Chunks 自动被阈值拦截过滤，降级机制生效。

### 步骤 6：RAG Chat 接口闭环与会话历史衔接 (B8, B9)
- **目标**：打通具有会话历史衔接和 Citation 二次校验清洗的高可信 RAG 聊天接口。
- **执行动作**：
  1. 编写 `/api/v1/chat/rag` 接口，支持传入 `conversationId` 和可选的 `documentIds`（限定过滤检索范围）。
  2. 后端负责基于 `conversationId` 从数据库拉取最近 10 条消息（不依赖前端直传 history），并将问答结果存入 existing messages 集合，保持连续。
  3. 编写正则匹配，物理拼装 output 中的 `citations`，计算 `citationIntegrity` 引用完整度，清洗大模型编造出的越界非法脚标。
  4. 编写 `DELETE /api/v1/documents/{documentId}`：若 chunks 较多，将元数据更新为 `deleting` 并投递清理作业至 Cloud Tasks 分批删除，防止超时。
- **可验证标准 (Green Light)**：
  - 通过 API 工具发送 POST 请求 `/api/v1/chat/rag`，输入 `"根据我的训练计划下周二训练什么项目？"`。
  - 返回大模型带有 `[1]` 脚标的 Answer，且响应 JSON 的 citations 节点成功包含了引用 1 的文档名、页码、正文 snippet。
  - 引用完整度指标 `citationIntegrity` 显示为 `"valid"`（若故意引诱大模型输出虚假引用，则状态自动计算为 `"invalid_cleaned"` 且虚假脚标已被后端清洗抹除）。
  - 对话数据已被物理存入对应的会话 messages 集合，前台可以正常加载。

---

## 阶段四：前端交互集成 (体验闭环)

### 步骤 7：知识库控制台面板与拖拽上传 (F1, F2)
- **目标**：在 Next.js 页面上让用户能管理其个人知识文件。
- **可验证标准 (Green Light)**：
  - 拖拽上传文件后，前台呈现进度条，状态进入 processing。前端启动轮询（Polling），大、中、小文件分别在各自 SLA 时效内（小文件15s、中文件45s、大文件90s）静默变绿显示为 success。

### 步骤 8：RAG 聊天组件升级 (F3)
- **目标**：实现聊天窗口中对 `[1]` 脚标的渲染，提供浮动 Tooltip 原文预览和弹窗溯源。
- **可验证标准 (Green Light)**：
  - 提问后，回答文本中的 `[1]` 以交互高亮链接渲染，鼠标悬浮呈现 Tooltip 框展示原文预览，点击优雅弹窗展现该 Chunk 详细信息，整个体验精致闭环。

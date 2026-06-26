# Phase 3 MVP 任务分解表 (MVP WBS Tasks)

为了将 Phase 3 方案落实到具体的人天级开发单元，我们制定了以下工作分解结构 (WBS)。任务覆盖技术 Spike、后端开发、前端开发、以及运维配置三个维度。

---

## 零、 技术 Spike (首要防线)

在进入任何核心向量业务逻辑开发前，必须先完成 Firestore 向量检索的前置技术 Spike，验证技术链条。

| 任务 ID | 任务名称 | 具体开发要求与规范 | 预估工时 |
| :--- | :--- | :--- | :--- |
| **S1** | **Spike: Firestore 向量最近邻技术验证** | **核心验证目标（必须全部跑通，否则不开启 B6/B7 的正式开发）：**<br>1. 验证在 C# 中成功写入 768 维的原生 `VectorValue`。<br>2. 通过 GCP 控制台或 gcloud 命令行成功部署 `chunks` 集合的复合向量索引。<br>3. 在 Spike 中，通过本地 HttpClient 或 curl 命令行对 REST runQuery 统一检索路径（建议路径：`POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery`）进行端到端实测验证，**该路径在未经实测验证前不得表述为最终正确路径**。Spike 阶段必须实测对齐与验证：parent path 结构、from collectionId=chunks 匹配、distanceResultField 属性定义、VectorValue 格式传输、以及返回 JSON parser 的正确解析能力。<br>4. 确认向量检索范围能严格限制在具体的子集合路径 `users/{uid}/chunks` 目录下，排除跨用户越权。<br>5. 验证 Cloud Run 服务绑定的 Service Account 确实拥有执行 `runQuery` 和 Firestore 读写、复合索引利用的所有 IAM 权限。 | 0.5 天 |

---

## 一、 后端开发任务 (.NET 10 Web API)

| 任务 ID | 任务名称 | 具体开发要求与规范 | 预估工时 |
| :--- | :--- | :--- | :--- |
| **B1** | 实体模型与元数据 Repository 底层构建 | 1. 在 `LifeAgent.Api/Models` 中创建强类型实体：`Document` 和 `Chunk`。<br>2. `Document` 的 `status` 字段中支持 `"deleting"` 异步删除状态。 | 0.5 天 |
| **B2** | GCS 物理存储安全服务层 | 1. 实现 `GoogleCloudStorageService.cs` 物理中转层。<br>2. 文件在 GCS 的写入、拉取、删除逻辑中强制前缀 `users/{userId}/...` 进行安全物理解耦。 | 0.5 天 |
| **B3** | 文档安全上传与元数据 API (同步阶段) | 1. 编写 Minimal API `POST /api/v1/documents` 接口。<br>2. 实现严格的安全过滤：前端文件大小限制 10MB，MIME 限制 PDF/TXT/MD。<br>3. 上传接口在物理中转写入 GCS 完毕，并向 Firestore 初始化一条状态为 `"processing"` 的元数据后，**向 Google Cloud Tasks 投递 Ingestion 处理作业**，投递成功后，接口立即返回 `202 Accepted`。 | 1.0 天 |
| **B4** | Google Cloud Tasks 异步触发内部端点安全保护 | 1. 设计并实现回调 Endpoint `/internal/api/v1/documents/process` 的 **OIDC Token 拦截与三重校验**：后端拦截器必须校验 Authorization Bearer OIDC Token 签名、验证 audience (当前 Cloud Run 托管 URL)、issuer (必须为 `https://accounts.google.com`) 以及调用方专用 service account 身份。`X-Appengine-QueueName` 仅作为辅助审计，绝不作为鉴权依据。<br>2. **落实 Worker Payload 不可信防御原则**：后端严禁盲信 Payload 里的 `userId`、`documentId`、`gcsPath`。必须根据数据库中的 documentId 重新读取元数据并校验：① 校验元数据的 `userId` 是否与 payload 完全一致；② 校验 `gcsPath` 格式和前缀必须完全符合 `users/{userId}/documents/{documentId}/`；③ **只有状态 status 确实为 `"processing"` 的文档才允许启动解析**，防止二次处理及被逻辑删除任务篡改。 | 0.5 天 |
| **B5** | 标准文本抽取与分块引擎 | 1. 引入 `PdfPig` 开源解析 PDF 文件，Txt/Md 标准按行抓取，提取出 Raw Text。<br>2. 封装 `IChunker`，实现“语义段落优先（段落 `\n`）+ 800 字符近似合并 + 10% overlap”的分块逻辑，并提取 `pageNumber`, `sectionTitle`, `charStart`, `charEnd` 索引元数据。 | 1.0 天 |
| **B6** | Gemini Embedding 向量持久化 | 1. 编写 `IEmbeddingService`，利用 `HttpClient` 调用 Google 终点（模型：`gemini-embedding-001`）。<br>2. **调用时必须在 Body 中显式设定 `outputDimensionality = 768`**。<br>3. 将计算出的 768d Vector 保存至 Firestore `chunks` 对应用户的子集合中。 | 1.0 天 |
| **B7** | 向量最近邻相似度检索服务 (阈值可配置与日志) | 1. 编写最近邻检索核心。基于 S1 Spike 的结果，利用 Native SDK 检索或基于 REST API StructuredQuery 的 runQuery 降级 HttpClient 伪代码方案。<br>2. **相似度阈值配置化**：余弦距离 `0.35`（相似度得分 `0.65`）仅作为 Phase 3 的初始默认值，绝不允许硬编码为不可调整的常量。必须支持通过 `appsettings.json` 或环境变量动态配置相似度距离过滤阈值。<br>3. **检索审计日志**：RAG 检索模块在运行时必须将检索到的 topK 分块的实际 `distance` / `score` 记录于详细的系统日志中，以便生产调参。 | 1.0 天 |
| **B8** | RAG 问答与 Citation 解析及会话对接 | 1. Minimal API `/api/v1/chat/rag` 请求体支持 `conversationId` 和可选的 `documentIds`。<br>2. **会话历史衔接**：后端根据 `conversationId` 从数据库拉取最近 10 条历史消息作为上下文，不依赖前端直传；大模型回答作为新 Message 存入对应的 messages 集合。<br>3. **引用二次校验与质量度评估**：正则比对引用脚标，清洗过滤越界引用，物理拼装 output 中的 `citations` 元数据，并计算 `citationIntegrity`。 | 1.5 天 |
| **B9** | 异步级联清理删除服务 | 1. 编写 `DELETE /api/v1/documents/{documentId}` API。<br>2. **异步清理机制**：若 chunks 较多，将元数据更新为 `deleting` 并投递清理作业至 Cloud Tasks。Worker 端点在后台物理清空 GCS 对象、并在后台采用 Limit 分页分批（每批 100）删除 `chunks`（不突破 500 单次限制），最终完全抹除 documents 元数据。 | 1.0 天 |

---

## 二、 前端开发任务 (Next.js 15 + Tailwind v4)

| 任务 ID | 任务名称 | 具体开发要求与规范 | 预估工时 |
| :--- | :--- | :--- | :--- |
| **F1** | 知识库控制台 Dashboard | 1. 开发“个人知识库管理”控制面板。<br>2. 展示表格：文件名、文件大小、创建时间、处理状态（采用精美小气泡：success、processing、deleting、failed）。<br>3. 支持删除，支持局部 Polling 轮询对齐 processing 和 deleting 的后台状态。 | 1.5 天 |
| **F2** | 安全文件上传组件 | 1. 设计拖拽式文件上传组件。<br>2. 严格在前端进行安全性拦截：限制文件大小最大 10MB，限制 MIME 格式仅 PDF、TXT、MD。 | 1.0 天 |
| **F3** | RAG 对话 UI 升级集成 | 1. 升级 Chat 面板。向后端 POST 接口时携带 `conversationId`。<br>2. **引用脚标渲染**：正则匹配大模型文本中的 `[1]` 脚标，渲染为可交互超链接。鼠标移入时 Tooltip 呈现对应的文档名和原文 snippet 预览；点击时弹窗展示对应 Chunk 的详情内容。 | 2.5 天 |

---

## 三、 运维配置与基础设施任务 (GCP Config)

| 任务 ID | 任务名称 | 具体开发要求与规范 | 预估工时 |
| :--- | :--- | :--- | :--- |
| **O1** | GCS 物理 Bucket 初始化 | 1. 创建物理 Bucket：`lifeos-user-knowledge-dev` / `lifeos-user-knowledge-prod`。<br>2. **区域完全与 Cloud Run 所在 Region 匹配**。 | 0.2 天 |
| **O2** | Firestore 向量索引部署 | 1. 部署 `chunks` 集合的复合向量索引，维度固定 768，度量 COSINE。<br>2. 用于 S1 Spike 验证。 | 0.2 天 |
| **O3** | Cloud Tasks 队列创建与 IAM | 1. 创建 RAG 专用的后台任务队列 `lifeos-ingestion-queue`。<br>2. 授权 Cloud Run 绑定的服务账户对 GCS Bucket 的 `storage.objectAdmin` 权限，以及对 Cloud Tasks 的投递权限。 | 0.2 天 |

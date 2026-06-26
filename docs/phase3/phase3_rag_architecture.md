# Phase 3 RAG 整体架构设计 (RAG Architecture)

## 一、 整体设计理念

LifeOS Phase 3 的知识接入层专为**个人小规模知识库场景（单用户累计 Chunks 在数万条以下）**设计。本系统采用 **Google Cloud Storage (文件存储) + Firestore 原生向量检索 (相似度匹配) + Google Cloud Tasks (后台异步队列) + Gemini API** 的 Serverless 架构，预期可满足 MVP 场景，需在验证阶段确认。

---

## 二、 核心数据流

系统由两大核心流构成：**1. 知识异步接入落库流 (Ingestion Pipeline)** 和 **2. 检索问答流 (Retrieval & Query Pipeline)**。

### 1. 知识异步接入落库流 (Ingestion Pipeline)

为了解决 Cloud Run 无状态容器随时缩容或在请求结束后挂起 CPU 的特性，本方案**明确禁止在 API 响应结束后直接采用 `Task.Run` 做 fire-and-forget 后台处理**。必须通过 Google Cloud Tasks 将处理任务异步排队，并在高吞吐或高延迟时提供可靠的任务触发保障。

```mermaid
flowchart TD
    User["用户 (Frontend)"] -->|1. POST /api/v1/documents| WebAPI["Web API (BFF Controller)"]
    WebAPI -->|2. 文件数据写入| GCS["Google Cloud Storage\n(users/{uid}/documents/doc_id/...)"]
    WebAPI -->|3. 创建元数据 (Status=processing)| FirestoreDocs["Firestore documents 集合\n(users/{uid}/documents)"]
    
    WebAPI -->|4. 投递 Ingestion Job| CloudTasks["Google Cloud Tasks\n(持久化后台任务队列)"]
    WebAPI -->|5. 立即返回 202 Accepted| User
    
    CloudTasks -->|6. POST HTTP 触发内部端点\n携带 OIDC JWT Token 鉴权| WorkerAPI["Web API (Internal Process Endpoint)"]
    
    subgraph "异步处理单元 (Worker API Context)"
        WorkerAPI -->|7. 安全拦截与校验\n1. OIDC Token 签名、issuer与aud 校验\n2. 查库不盲信 Payload, 校验 userId 和 status| SafetyVerify{"安全合规校验"}
        SafetyVerify -->|"通过"| Parser["PdfPig / Txt / Md Parser\n+ 段落优先合并 (Overlap)"]
        Parser -->|8. 语义 Chunks 文本| EmbedSvc["Embedding Service"]
        EmbedSvc -->|9. 调用 gemini-embedding-001\n(outputDimensionality=768)| GeminiAPI["Gemini Embedding API"]
        GeminiAPI -->|10. 返回 768d Vector| EmbedSvc
        EmbedSvc -->|11. 批量写入 Chunks 与向量| FirestoreChunks["Firestore chunks 集合\n(users/{uid}/chunks)"]
        WorkerAPI -->|12. 更新文档状态 (Status=success)| FirestoreDocs
    end
```

### 2. 检索问答流 (Retrieval & Query Pipeline)

检索问答流严格衔接已有的会话历史，通过拉取数据库消息和双轨制 nearest-neighbor 检索、相似度阈值过滤，将高质量上下文提供给大模型：

```mermaid
flowchart TD
    User["用户提问 (Question)"] -->|1. POST /api/v1/chat/rag\n(conversationId, clientTimeZone, documentIds)| WebAPI["Web API (BFF Controller)"]
    
    subgraph "检索准备与历史恢复"
        WebAPI -->|2. 依 conversationId 自数据库拉取最近 N 条历史消息| FirestoreMsgs["Firestore messages 集合\n(users/{uid}/conversations/.../messages)"]
        WebAPI -->|3. 问题向量化 gemini-embedding-001| GeminiEmbed["Gemini Embedding API\n(outputDimensionality=768)"]
    end
    
    GeminiEmbed -->|4. 返回 768d Vector| WebAPI
    
    subgraph "相似度检索与强物理过滤"
        WebAPI -->|5. 执行 nearest-neighbor 检索| FirestoreChunks["Firestore chunks 集合\n(users/{uid}/chunks)"]
        FirestoreChunks -->|6. Top-K Chunks| ThresholdFilter{"相似度距离阈值比对\n(由配置文件 appsettings 动态调节)"}
    end
    
    ThresholdFilter -->|"有高相关 Chunks"| ContextPrompt["将命中 Chunks 作为参考拼入 Prompt"]
    ThresholdFilter -->|"全低相关 Chunks"| EmptyPrompt["空上下文，触发大模型安全拒绝"]
    
    subgraph "大模型生成与引用校验"
        ContextPrompt & EmptyPrompt -->|7. 输送 Context + History + Q| GeminiFlash["Gemini 2.5 Flash"]
        GeminiFlash -->|8. 输出 Markdown 文本 (含引用标号 [1])| CitationParser["后端 Citation 质量校验模块"]
        CitationParser -->|9. 校验 retrievedChunks\n生成 citationIntegrity 元数据"]
    end
    
    CitationParser -->|10. 写入数据库并返回前端| User
```

---

## 三、 核心安全防御与不可信防护机制

1. **异步回调端点 OIDC 安全拦截 (OIDC Authentication)**:
   - 内部回调端点 `/internal/api/v1/documents/process` 将受到 Google OIDC Bearer Token 校验保护。
   - Cloud Tasks 投递时生成 OIDC JWT，后端对该 Token 的 Signature, Issuer, Audience (aud), 及绑定的 Service Account 账号进行双重身份审计，**拒绝仅依赖请求头 X-Appengine-QueueName 的不安全鉴权。**

2. **Worker 载荷不信任原则 (Zero-Trust Payload Verification)**:
   - **不盲目采信** Cloud Tasks 请求体里的 `userId`, `documentId`, `gcsPath`。
   - 收到请求后，后端必须用 `documentId` 为 Key 检索 Firestore。确认该文档真实的 metadata 是否处于 `"processing"` 状态。
   - 校验元数据中实际记录的 `userId` 与 Payload 是否完全一致。
   - 检查 GCS 物理路径前缀是否完全处于 `users/{userId}/documents/{documentId}/` 特定的多租户目录结构下，否则拒绝抽取，防止任意路径文本提取引起的注入漏洞。

3. **检索日志监控与相似度阈值可配性**:
   - 相似度距离门限不写死。通过应用程序配置文件中 `RAG:DistanceThreshold` 参数注入，并在检索日志中详细打出每一条 `topK distance / score` 审计指标，利于后续生产调优。

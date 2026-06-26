# Phase 3 RAG 与知识接入层 非目标规范 (Non-Goals)

为了在快速迭代中保持焦点，防范范围蔓延 (Scope Creep) 和过度设计 (Over-engineering)，本设计文档明确界定了 **Phase 3 的非目标 (Non-Goals)**。以下列出的功能均不在本阶段的设计和实现范围内，将被安全地留存到 Phase 4 或后续的长期演进中。

---

## 一、 架构与数据库级非目标

### 1. 跨租户/跨用户全局联合检索 (Cross-Tenant Retrieval)
- **非目标描述**: 严禁支持任何跨用户、跨群组、跨部门的全局向量检索和数据共享合并。
- **定位范围**: Phase 3 专注于严格的单用户个人知识库。向量查询范围绝对限制在当前 UID 绑定的 `users/{uid}/chunks` 目录下进行，不考虑任何群组协作、知识库转授权或组织级统一检索。

### 2. 外部复杂分布式向量数据库接入 (Enterprise Vector DBs)
- **非目标描述**: 不引入额外的分布式向量数据库，如 Pinecone (商用版)、Milvus、Qdrant，或是 Cloud SQL PostgreSQL pgvector、AlloyDB 托管服务。
- **定位范围**: Phase 3 MVP 一律基于 **Firestore 原生向量检索 (Nearest-Neighbor 相似度匹配)** 及双轨 REST API 降级架构运行。这满足个人小规模数据量要求，也最大化精简了多余数据库组件。

### 3. 多路召回与高级重排 (Multi-Path Retrieval & Advanced Reranking)
- **非目标描述**: 不进行多路召回（关键词 BM25 与向量相似度的 Reciprocal Rank Fusion），不接入额外的重排模型（如 Cohere Rerank、Vertex AI Rerank、BGE-Reranker）。
- **定位范围**: 检索层直接基于 `gemini-embedding-001` 特征相似度，计算 Cosine 余弦距离返回 Top-5 结果。在 Phase 3 的语境下，该最简语义匹配预期可满足 MVP 场景，需在验证阶段确认。

### 4. 默认可一键切换的高级 Embedding 模型
- **非目标描述**: 不支持多 Embedding 模型的动态热切换或一键转换机制。
- **定位范围**: 本阶段强制锁定 `gemini-embedding-001`。对于更先进的 `gemini-embedding-2`，只保留在未来的技术调研和技术储备中，不做任何运行时接口的路由。

---

## 二、 文本提取与分块非目标

### 1. 扫描件高级 OCR 与多模态物理读取 (Scanning PDF OCR & Multimodal Ingestion)
- **非目标描述**: 不包含对纯图片扫描版 PDF、加密 PDF、手写体识别的高级 OCR 自动处理。
- **定位范围**: Phase 3 仅基于 `PdfPig` 等原生开源类库抽取出内含的标准文字。若遇到图片或密码保护，作置灰/failed 处理，提示用户上传标准文本 PDF。

### 2. 精确的 Token-Aware 动态分块
- **非目标描述**: 在 Chunk 划分逻辑中，不引入复杂的 Tokenizer 计算引擎（例如 C# 社区的 SharpToken 动态实时计算 Token 长度并精细切分）。
- **定位范围**: Phase 3 MVP 通过基于段落（换行符）和字符合并（字数近似）的段落切片方式进行，对 Token-Aware 精准切片予以延后。

---

## 三、 用户交互与产品功能级非目标

### 1. 外部云盘、第三方应用定时自动同步 (External Ingestion Synchronizer)
- **非目标描述**: 不开发与 Google Drive、Microsoft OneDrive、Notion、Obsidian、GitHub 等外部 SaaS 工具的数据实时监听、动态推送、定时抓取和自动对齐同步机制。
- **定位范围**: 仅支持用户主动在 LifeOS 网页端手动上传本地文件，以实现知识库的更新。

### 2. 知识图谱生成与高交互在线标注 (Knowledge Graph & In-line Annotation)
- **非目标描述**: 前端不开发类似于在线 PDF 查看器中的内容框选、高亮手绘、段落批注标注，不进行实体关系抽取 (NER) 并在前台生成知识图谱 (Graph) 可视化星盘。
- **定位范围**: 前端页面聚焦于最经典的知识库表格管理（上传/删除/状态）和升级后的 RAG Chat 问答框。

---

## 四、 智能体动态决策级非目标

### 1. 自主 Agent 工具调用判定 (Dynamic Tool-Use / Function Calling)
- **非目标描述**: 在聊天会话中，大模型不需要自主、动态判断“这一轮对话需不需要调 RAG 检索工具”、“需不需要结合多个不同的文档库进行混合多步骤决策”。
- **定位范围**: Phase 3 专注于建立稳定、鲁棒的“知识接入通道”。在 `/api/v1/chat/rag` 请求中，后端将高确定性地对每一次用户提问执行向量化、向量检索、Prompt 拼装。动态决策、真正的智能 Agent Tool-Use 将作为 Phase 4 的核心突破口。

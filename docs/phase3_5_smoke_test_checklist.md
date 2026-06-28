# Phase 3.5 关键链路冒烟验证清单

## 1. 系统关键链路梳理

### 1.1 认证链路

| 链路 | 涉及组件 | 说明 |
|---|---|---|
| 用户登录 | 前端 Firebase Auth → ID Token | Google Sign-In 获取 token |
| Token 传递 | 前端 Server Action → Cookie → Bearer Header | HttpOnly cookie 转发 |
| Token 验证 | `FirebaseAuthMiddleware` | Firebase Admin SDK 验签 |
| 用户隔离 | `HttpContext.Items["userId"]` | 所有 endpoint 通过 userId 隔离 |

### 1.2 RAG 文档处理链路

| 链路 | 涉及组件 | 说明 |
|---|---|---|
| 文档上传 | `POST /api/v1/documents` → FileValidator → GCS | 文件校验 + 云存储 |
| 异步处理 | Cloud Tasks → `POST /internal/api/v1/documents/process` | OIDC 认证回调 |
| 文本抽取 | `IDocumentTextExtractor` (PdfPig) | PDF/TXT/DOCX 文本提取 |
| 文本分块 | `IChunker` (BasicChunker) | 按页/段落分块 |
| 向量生成 | `IEmbeddingService` (Gemini text-embedding-004) | 768 维向量 |
| 向量写入 | `IFirestoreVectorStore` (REST API) | Firestore chunks 集合 |
| 状态更新 | `IDocumentRepository` | processing → success/failed |

### 1.3 RAG 问答链路

| 链路 | 涉及组件 | 说明 |
|---|---|---|
| 用户提问 | `POST /api/v1/chat/rag` | 接收用户消息 |
| 问题 Embedding | `IEmbeddingService` | 问题向量化 |
| 向量检索 | `IFirestoreVectorStore.FindNearestAsync` | COSINE 相似度搜索 |
| 上下文组装 | `RagChatService` | 拼接 prompt + 上下文 |
| LLM 回答 | `IRagAnswerGenerator` (Gemini) | 生成回答 |
| 引用处理 | `CitationProcessor` | 验证引用有效性 |
| 历史保存 | `IChatSessionRepository` | 保存对话记录 |

### 1.4 成本保护链路

| 保护 | 组件 | 说明 |
|---|---|---|
| 文件大小限制 | `FileValidator` (10MB) + Kestrel (15MB) | 双层防护 |
| Chunk 数量上限 | `MaxChunksPerDocument` (200) | 超限截断 + IsTruncated |
| 每日 LLM 配额 | `DailyQuotaService` (200/日) | per-user 计数 |
| 每日 Embedding 配额 | `DailyQuotaService` (500/日) | per-user 计数 |
| 每日文档处理配额 | `DailyQuotaService` (20/日) | per-user 计数 |
| API Rate Limiting | ASP.NET Core RateLimiter | per-user/per-IP 分钟级限流 |

### 1.5 基础设施链路

| 链路 | 组件 | 说明 |
|---|---|---|
| 健康检查 | `GET /health` | Cloud Run 实例健康探针 |
| 错误处理 | `ExceptionMiddleware` | 全局异常捕获 + 格式化响应 |
| 多租户隔离 | Firestore path `users/{userId}/...` | 数据路径隔离 |

---

## 2. 冒烟验证清单

### 2.1 认证链路

| # | 验证项 | 目的 | 测试步骤 | 期望结果 | 需要认证 | 产生成本 | 可线上执行 | 建议环境 | 风险等级 |
|---|---|---|---|---|---|---|---|---|---|
| A1 | /health 不需认证 | 确认健康检查不被 Auth 拦截 | `curl /health` | 200 "healthy" | 否 | 无 | ✅ | 线上 | 低 |
| A2 | 未认证访问返回 401 | 确认 Auth 中间件生效 | `curl /api/life/events` | 401 Unauthorized | 否 | 无 | ✅ | 线上 | 低 |
| A3 | 伪造 token 返回 401 | 确认 token 验签生效 | `curl -H "Authorization: Bearer fake" /api/life/events` | 401 | 否 | 无 | ✅ | 线上 | 低 |
| A4 | Mock token 登录 | 确认 Development Mock 模式 | `curl -H "Authorization: Bearer mock_local_token_123" /api/life/events` | 200 + 用户数据 | 是 | 无 | ❌ | 本地 | 低 |
| A5 | 真实 Firebase token 登录 | 确认 Production 认证 | 浏览器登录后调用 API | 200 + 用户数据 | 是 | 无 | ✅ | 线上 | 低 |

### 2.2 RAG 文档处理链路

| # | 验证项 | 目的 | 测试步骤 | 期望结果 | 需要认证 | 产生成本 | 可线上执行 | 建议环境 | 风险等级 |
|---|---|---|---|---|---|---|---|---|---|
| B1 | 上传小 PDF | 确认上传 + GCS 存储 | 上传 100KB PDF | 202 Accepted + documentId | 是 | 低 | ✅ | 线上 | 低 |
| B2 | 文档状态查询 | 确认元数据写入 | `GET /api/v1/documents` | 返回文档列表 + status | 是 | 无 | ✅ | 线上 | 低 |
| B3 | 文档处理回调 | 确认 Cloud Tasks → internal endpoint | 等待异步处理完成 | status: processing → success | 是 | 中 | ✅ | 线上 | 中 |
| B4 | 文档文本抽取 | 确认 PDF 文本提取 | 查看处理日志 | 文本抽取成功 | 是 | 低 | ✅ | 线上 | 低 |
| B5 | 文档分块 | 确认 chunk 生成 | 查看处理日志 | chunks > 0 | 是 | 低 | ✅ | 线上 | 低 |
| B6 | Embedding 生成 | 确认向量写入 | 查看 Firestore chunks 集合 | 768 维向量存在 | 是 | 中 | ✅ | 线上 | 中 |
| B7 | 文档删除 | 确认 GCS + 元数据清理 | `DELETE /api/v1/documents/{id}` | 200 + 文档删除 | 是 | 无 | ✅ | 线上 | 低 |

### 2.3 RAG 问答链路

| # | 验证项 | 目的 | 测试步骤 | 期望结果 | 需要认证 | 产生成本 | 可线上执行 | 建议环境 | 风险等级 |
|---|---|---|---|---|---|---|---|---|---|
| C1 | RAG 问答基本功能 | 确认端到端 RAG 流程 | 发送简单问题 | 200 + 回答 + citations | 是 | 高 | ✅ | 线上 | 中 |
| C2 | 引用 citation 返回 | 确认引用有效性 | 检查回答中的 citation | 有效引用 + 文件名 | 是 | 高 | ✅ | 线上 | 中 |
| C3 | 历史消息读取 | 确认会话持久化 | `GET /api/v1/chat/rag/{id}/messages` | 返回历史消息 | 是 | 无 | ✅ | 线上 | 低 |
| C4 | 清除历史消息 | 确认会话清理 | `DELETE /api/v1/chat/rag/{id}/messages` | 200 + 消息删除 | 是 | 无 | ✅ | 线上 | 低 |
| C5 | 无文档时 RAG 问答 | 确认无结果时的行为 | 在无文档时提问 | 200 + "未找到相关文档" 类回答 | 是 | 高 | ✅ | 线上 | 中 |

### 2.4 成本保护链路

| # | 验证项 | 目的 | 测试步骤 | 期望结果 | 需要认证 | 产生成本 | 可线上执行 | 建议环境 | 风险等级 |
|---|---|---|---|---|---|---|---|---|---|
| D1 | 超大文件拒绝 (>10MB) | 确认 FileValidator 生效 | 上传 11MB 文件 | 400/413 拒绝 | 是 | 无 | ✅ | 线上 | 低 |
| D2 | Kestrel 请求体限制 (>15MB) | 确认 Kestrel 兜底 | 发送 16MB 请求 | 413 Request Entity Too Large | 否 | 无 | ✅ | 线上 | 低 |
| D3 | 每日 LLM 配额耗尽 | 确认配额保护 | 调用 200+ 次 ingest | 429 配额超限 | 是 | 高 | ❌ | 本地 | 高 |
| D4 | 每日 Embedding 配额耗尽 | 确认配额保护 | 调用 500+ 次 RAG | 429 配额超限 | 是 | 高 | ❌ | 本地 | 高 |
| D5 | Chunk 截断 (>200) | 确认 MaxChunksPerDocument | 上传大文档 | chunks ≤ 200, IsTruncated=true | 是 | 中 | ✅ | 线上 | 中 |
| D6 | Rate Limit 超限 | 确认分钟级限流 | 快速发送 10+ 次 high-cost 请求 | 429 请求过于频繁 | 是 | 高 | ❌ | 本地 | 高 |

### 2.5 Internal 端点安全

| # | 验证项 | 目的 | 测试步骤 | 期望结果 | 需要认证 | 产生成本 | 可线上执行 | 建议环境 | 风险等级 |
|---|---|---|---|---|---|---|---|---|---|
| E1 | 无 token 拒绝 | 确认 OIDC 校验 | `curl POST /internal/.../process` | 401 Missing token | 否 | 无 | ✅ | 线上 | 低 |
| E2 | 伪造 token 拒绝 | 确认 OIDC 验签 | `curl -H "Authorization: Bearer fake" POST /internal/.../process` | 401 OIDC validation failed | 否 | 无 | ✅ | 线上 | 低 |
| E3 | 跨租户请求拒绝 | 确认零信任校验 | 请求中 userId ≠ 文档所有者 | 403 Access denied | 是 | 无 | ❌ | 本地 | 低 |
| E4 | GCS 路径伪造拒绝 | 确认路径前缀校验 | 请求中 gcsPath 不匹配 | 403 Path validation failed | 是 | 无 | ❌ | 本地 | 低 |

### 2.6 基础设施

| # | 验证项 | 目的 | 测试步骤 | 期望结果 | 需要认证 | 产生成本 | 可线上执行 | 建议环境 | 风险等级 |
|---|---|---|---|---|---|---|---|---|---|
| F1 | Cloud Run 健康 | 确认实例正常 | `gcloud run services describe` | Ready | 否 | 无 | ✅ | 线上 | 低 |
| F2 | Traffic 100% | 确认流量指向最新 | 检查 traffic 配置 | 100% latest revision | 否 | 无 | ✅ | 线上 | 低 |
| F3 | Git 状态干净 | 确认无未提交改动 | `git status --short` | 空 | 否 | 无 | ✅ | 本地 | 低 |
| F4 | 单元测试通过 | 确认代码质量 | `dotnet test` | 全部通过 | 否 | 无 | ✅ | 本地 | 低 |

---

## 3. 手动验证步骤（高成本项目）

以下项目不适合自动化执行，需手动验证：

### 3.1 上传 PDF 并完成 RAG 问答（端到端）

**前置条件**：已登录，有小体积测试 PDF

1. 通过 Web UI 或 API 上传 100KB PDF
2. 确认返回 202 + documentId
3. 等待 1-3 分钟（Cloud Tasks 异步处理）
4. `GET /api/v1/documents` 确认 status 变为 "success"
5. `POST /api/v1/chat/rag` 发送与文档内容相关的问题
6. 确认回答包含文档内容引用
7. 确认 citations 中有文件名和页码

**预期成本**：~1 次文档处理 + ~1 次 RAG（embedding + LLM）

### 3.2 每日配额耗尽验证

**前置条件**：本地 Development 环境，配额设置为较小值

1. 在 `appsettings.Development.json` 临时设置 `DailyLlmCallLimit: 2`
2. 启动本地 API
3. 调用 `POST /api/life/ingest` 3 次
4. 第 3 次应返回 429 + "今日 AI 调用次数已达上限"
5. 恢复配置

**预期成本**：~2 次 LLM 调用

### 3.3 Rate Limit 超限验证

**前置条件**：本地 Development 环境，限流设置为较小值

1. 在 `appsettings.Development.json` 临时设置 `HighCost.PermitLimit: 3`
2. 启动本地 API
3. 快速调用 `POST /api/v1/chat/rag` 4 次
4. 第 4 次应返回 429 + "请求过于频繁"
5. 恢复配置

**预期成本**：~3 次 RAG 调用

### 3.4 大文档 Chunk 截断验证

**前置条件**：有超过 200 chunk 的大文档（~50 页以上 PDF）

1. 上传大 PDF
2. 等待处理完成
3. `GET /api/v1/documents` 确认 `isTruncated: true`
4. 确认 `chunkCount: 200`

**预期成本**：~1 次文档处理 + 200 次 embedding

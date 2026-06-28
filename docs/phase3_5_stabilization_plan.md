# Phase 3.5 稳定化计划

> 创建日期：2026-06-27
> 前置条件：Phase 1/2/3 已全部完成，部署文档已就绪
> 后续目标：Phase 4 Agent 化 MVP（每日复盘 Agent）

---

## 1. 阶段目标

Phase 3.5 **不新增业务功能**，不大改现有架构。核心目标是：

| 维度 | 目标 |
|------|------|
| **安全** | 确认 Firestore Security Rules、API 身份校验链路完整性 |
| **成本控制** | 为 Gemini / Embedding 调用设置用户级限流和预算保护，防止意外费用失控 |
| **测试覆盖** | 前端组件基础测试（Vitest）、关键流程 E2E 测试（Playwright）从零开始补齐 |
| **部署稳定性** | 固化部署流程，建立线上冒烟验证清单 |
| **RAG 质量验证** | 建立引用准确性样例集，量化 RAG 回答质量基线 |
| **可观测性** | 明确线上问题发现和定位方式 |

**完成判断**：P0 项全部完成 + P1 项至少完成 60% → 可进入 Phase 4。

---

## 2. P0 任务：必须优先完成

### 2.1 Firestore Security Rules 检查

**目标**：确认 Firestore 数据访问是否有第二层防护（除后端中间件外）。

**涉及文件/模块**：
- `firestore.rules` ✅ 已创建
- `firebase.json` / `.firebaserc` ✅ 已创建
- Firestore 数据模型：`users/{userId}/life_events|reminders|documents|chunks|chat_sessions|daily_summaries|agent_runs`
- 后端 `FirebaseAuthMiddleware` 的 token 校验逻辑

**实现状态**：

| 项目 | 状态 | 说明 |
|---|---|---|
| `firestore.rules` 文件 | ✅ 已创建 | 覆盖全部 8 个子集合，默认 deny，isOwner 校验 |
| `firebase.json` | ✅ 已创建 | 最小 CLI 配置 |
| `.firebaserc` | ✅ 已创建 | 指向 `copper-affinity-467409-k7` |
| 部署到线上 | ⏸️ 阻塞 | 跨项目 Auth 问题（见下文） |
| Rules Playground 验证 | ⏸️ 待部署后执行 | 10 个测试场景已设计 |
| Emulator 自动化测试 | ⏭️ 推迟到 Phase 4 | 无 emulator 基础设施 |

**⚠️ 跨项目 Auth 阻塞问题**：

当前架构使用两个独立 GCP 项目：
- **Firestore 数据项目**：`copper-affinity-467409-k7`（数据 + Cloud Run + Cloud Tasks）
- **Firebase Auth 项目**：`my-agent-app-a5e42`（用户认证）

Firestore Security Rules 的 `request.auth` 只能验证**同一项目**签发的 Auth token。如果直接部署 rules 到 `copper-affinity-467409-k7`，来自 `my-agent-app-a5e42` 的 token 会使 `request.auth == null`，导致所有请求被拒绝（default deny）。

**解决方案**（需后续执行）：
1. 在 `copper-affinity-467409-k7` 上启用 Firebase Auth（Identity Platform）
2. 配置 Google Sign-In provider
3. 更新前端 `NEXT_PUBLIC_FIREBASE_*` 环境变量指向新项目
4. 更新后端 `FirebaseApp.Create()` 的 projectId
5. 然后执行 `firebase deploy --only firestore:rules`

**验收标准**：
- [x] `firestore.rules` 文件存在且语法正确
- [ ] 部署到线上（需先解决跨项目 Auth）
- [ ] 未登录用户无法读取任何 `users/{userId}/` 路径
- [ ] 已登录用户 A 无法读取用户 B 的数据
- [ ] 客户端无法直接写入 `chunks` 集合（由后端服务账号写入）

---

### 2.2 API Rate Limiting 方案

**目标**：防止恶意或意外的高频调用导致后端过载和 LLM 费用失控。

**涉及接口**：
- `POST /api/life/ingest`（触发 LLM 解析）
- `POST /api/v1/documents`（触发文档上传 + 异步处理链路）
- `POST /api/v1/rag/chat`（触发 embedding + LLM 生成）
- `POST /api/summary/generate`（触发 LLM 生成）

**限制策略建议**：

| 层级 | 限制 | 说明 |
|------|------|------|
| 每用户每分钟 | LLM 类接口 ≤ 10 次/分钟 | 防止脚本刷接口 |
| 每用户每日 | LLM 类接口 ≤ 200 次/日 | 防止成本失控 |
| 全局 | 文档处理队列 ≤ 50 个并发 | 防止 Cloud Tasks 过载 |

**建议方案**：
- **第一阶段**：使用 ASP.NET Core 内存限流中间件（`Microsoft.AspNetCore.RateLimiting`），基于 `userId` 分桶
- **第二阶段**（如有需要）：接入 Cloud Armor 或 Redis 分布式限流
- 限流触发时返回 `429 Too Many Requests`，前端展示友好提示

**实现状态**：

| 项目 | 状态 | 说明 |
|---|---|---|
| RateLimiter 中间件 | ✅ 已实现 | ASP.NET Core 内置 `FixedWindowRateLimiter`，4 层 policy |
| 配置外部化 | ✅ 已实现 | `appsettings.json` 中 `Rag.RateLimiting` section |
| Development 宽松配置 | ✅ 已实现 | `appsettings.Development.json` PermitLimit=1000 |
| Endpoint 绑定 | ✅ 已实现 | high-cost 6 个端点，internal 1 个端点 |
| 测试 | ✅ 已实现 | 6 个配置模型测试 |
| 前端 429 处理 | ⏭️ 后续 | Phase 3.5 范围外，需前端改动 |

**限流策略详情**：

| Policy | 对象 | 窗口 | Production 限制 | 适用端点 |
|---|---|---|---|---|
| 不限流 | — | — | — | `GET /health`, `GET /` |
| `global-ip` | 未认证（按 IP） | 1 分钟 | 30/min | 未认证请求兜底 |
| `auth-user` | 已认证（按 userId） | 1 分钟 | 60/min | GET/PUT/PATCH/DELETE |
| `high-cost` | 已认证（按 userId） | 1 分钟 | 10/min | POST LLM/Upload |
| `internal` | 按 IP | 1 分钟 | 20/min | `/internal/*` |

**配置项**（`appsettings.json` → `Rag.RateLimiting`）：
- `GlobalIp.PermitLimit` / `WindowSeconds` / `QueueLimit`
- `AuthenticatedUser.PermitLimit` / `WindowSeconds` / `QueueLimit`
- `HighCost.PermitLimit` / `WindowSeconds` / `QueueLimit`
- `Internal.PermitLimit` / `WindowSeconds` / `QueueLimit`

**验收标准**：
- [x] LLM 类接口有每用户每分钟限流
- [x] 超限时返回 429 状态码
- [ ] 前端展示"请求过于频繁，请稍后重试"提示（后续）
- [x] 限流配置可通过 appsettings 调整（无需重新部署）

---

### 2.3 文件上传大小限制

**目标**：防止超大文件导致 GCS 存储成本和处理时间失控。

**当前状态**：
- 前端：已有 10MB 限制（`KnowledgeBase.tsx` 中的 `accept` 和文件大小检查）
- 后端：`FileValidator` 已做文件类型和路径遍历校验，需确认是否有大小限制

**建议限制值**：

| 层级 | 限制 | 说明 |
|------|------|------|
| 前端 | 10MB（已有） | 浏览器端拦截，用户即时反馈 |
| 后端 | 15MB | 作为安全兜底，防止绕过前端直接调 API |

**前端处理**：已有，无需改动（或确认现有实现正确）

**后端处理**：
- 在 `DocumentEndpoints` 的上传接口中添加 `MaxRequestBodySize` 限制
- 或在 `FileValidator` 中添加文件大小校验
- 超限时返回 `413 Payload Too Large` 或自定义错误

**验收标准**：
- [ ] 前端上传超过 10MB 的文件时，上传前拦截并提示
- [ ] 后端上传接口有 15MB 的请求体大小限制
- [ ] 超限时返回明确错误信息，不产生 GCS 残留文件

---

### 2.4 文档 chunk 数量限制

**目标**：防止单个超大文档产生过多 chunk，导致 embedding 调用次数和成本爆炸。

**涉及模块**：
- `BasicChunker`（文本分块逻辑）
- `InternalDocumentEndpoints`（文档处理 worker）

**建议限制策略**：

| 限制项 | 值 | 说明 |
|--------|-----|------|
| 单文档最大 chunk 数 | 500 | 基本覆盖 400KB 文本（800字/chunk） |
| 单文档最大页数 | 200 | 超过时截断并记录警告 |
| 超限处理 | 截断 + 标记 | 在 `KnowledgeDocument` 中标记 `isTruncated: true` |

**实现方式**：
1. 在 `BasicChunker.ChunkAsync` 中添加 chunk 数量计数
2. 达到上限时停止分块，记录警告日志
3. 在 `KnowledgeDocument` 元数据中更新 `chunkCount` 和 `isTruncated` 状态
4. 前端文档列表中展示截断标记（可选）

**验收标准**：
- [ ] 单文档最多产生 500 个 chunk
- [ ] 超限时截断而非报错，文档仍可用（只是内容不完整）
- [ ] 截断状态在文档元数据中可查
- [ ] 截断时有日志记录（便于监控和手动处理）

---

### 2.5 Gemini / Embedding 成本保护

**目标**：建立调用量监控和预算保护，避免意外费用。

**涉及模块**：
- `GeminiLlmService`（LLM 调用）
- `GeminiEmbeddingService`（Embedding 调用）
- `RagChatService`（RAG 问答，同时消耗 embedding + LLM）

**建议方案**：

| 保护层级 | 策略 | 说明 |
|----------|------|------|
| 用户级每日调用次数 | LLM ≤ 200 次/日，Embedding ≤ 500 次/日 | 内存计数器或 Firestore 计数文档 |
| 文档处理次数 | 每用户每日 ≤ 20 个文档 | 防止批量上传刷 embedding |
| 错误提示 | 超限时返回明确提示 | "今日 AI 调用次数已用完，请明天再试" |
| 预算提醒 | GCP Budget Alert | 在 GCP Console 设置月度预算警戒线（建议 $50/月） |

**实现方式**：
1. 在 `RateLimitingMiddleware`（2.2 产出）中同时统计 LLM/Embedding 调用次数
2. 使用 `IMemoryCache` 或 Firestore 文档记录每日调用计数
3. 超限时抛出自定义异常，由 `ExceptionMiddleware` 统一返回友好错误
4. GCP Console → Billing → Budgets & Alerts → 设置月度预算 $50，触发 80% / 100% 告警

**验收标准**：
- [ ] 每用户每日 LLM 调用有上限
- [ ] 每用户每日文档处理数量有上限
- [ ] 超限时返回友好错误提示（非 500 错误）
- [ ] GCP 预算告警已配置
- [ ] 限流计数在用户维度独立，用户 A 超限不影响用户 B

---

### 2.6 关键链路线上冒烟验证清单

**目标**：每次部署后，快速验证核心功能正常，不依赖自动化测试。

**冒烟清单**：

| # | 测试项 | 操作步骤 | 预期结果 |
|---|--------|---------|----------|
| 1 | **登录** | 访问 `life.zhuchaofan.com`，点击 Google 登录 | 成功跳转，显示用户名，进入主页面 |
| 2 | **文档上传** | 切换到"知识库管理"标签，上传一个 ≤ 5MB 的 PDF | 上传成功，文档列表中出现新文档，状态为"已处理" |
| 3 | **文档删除** | 在文档列表中点击删除按钮 | 二次确认弹窗出现，确认后文档从列表消失 |
| 4 | **文档选择** | 在"知识库问答"标签中勾选一个文档 | checkbox 选中状态正确 |
| 5 | **RAG 问答** | 选择已上传的文档，输入一个与文档内容相关的问题 | 返回回答，内容相关，无 500 错误 |
| 6 | **引用来源** | 查看 RAG 回答中的脚标 | 脚标可点击/悬浮，显示来源文档和原文段落 |
| 7 | **清除 RAG 对话** | 点击垃圾桶图标，确认清除 | 对话历史清空，页面恢复初始状态 |
| 8 | **移动端布局** | 使用手机浏览器或 Chrome DevTools 模拟移动端访问 | 页面无横向滚动条，内容可正常阅读 |
| 9 | **页面无横向溢出** | 在桌面端和移动端分别检查三个标签页 | 所有页面宽度不超过视口，无横向滚动条 |

**建议执行频率**：
- 每次 Cloud Run 部署后
- 每次合并到 `main` 分支前（可选，手动执行关键项 1/2/5）

**验收标准**：
- [ ] 冒烟清单文档化（本文档即为文档）
- [ ] 至少执行一次完整冒烟，记录结果
- [ ] 所有项通过后方可认为部署成功

---

## 3. P1 任务：建议完成

### 3.1 前端基础测试

**目标**：前端组件从零测试覆盖提升到基本覆盖，降低回归风险。

**涉及模块**：
- `IngestForm` — 摄入表单提交逻辑
- `Timeline` — 事件列表渲染、编辑、删除
- `ReminderWidget` — 提醒列表和状态操作
- `DailySummaryCard` — 总结展示和生成触发
- `KnowledgeBase` — 文档上传、列表、删除
- `RagChat` — 对话输入、历史加载、引用展示
- `Markdown` — Markdown 渲染和脚标处理

**建议实现方式**：
1. 安装 `vitest` + `@testing-library/react` + `@testing-library/jest-dom`
2. 为每个组件编写 smoke test（渲染不报错 + 关键元素存在）
3. 为交互逻辑编写行为测试（表单提交、按钮点击、状态变化）
4. 在 CI 中添加 `npm test` 步骤

**验收标准**：
- [ ] 7 个主要组件均有至少 1 个测试用例
- [ ] `npm test` 全部通过
- [ ] 测试覆盖核心交互逻辑（表单提交、删除确认、对话输入）

---

### 3.2 RAG 端到端测试

**目标**：验证 RAG 管线从文档上传到问答的完整链路。

**涉及模块**：
- 前端 `KnowledgeBase` → 后端 `DocumentEndpoints` → Cloud Tasks → `InternalDocumentEndpoints`
- 前端 `RagChat` → 后端 `RagChatEndpoints` → `RagChatService` → `RestFirestoreVectorStore`

**建议实现方式**：
1. 编写后端集成测试（使用 `WebApplicationFactory`）：
   - 上传测试文档 → 等待处理完成 → RAG 问答 → 验证回答包含相关内容
2. 使用 mock 或测试专用的 Gemini API key（避免真实调用成本）
3. 验证引文完整性（`citationIntegrity` 字段为 `valid`）

**验收标准**：
- [ ] 上传 → 处理 → 问答全链路有自动化测试
- [ ] 测试可在本地运行（使用 mock 服务）
- [ ] 引文完整性验证通过

---

### 3.3 核心流程 E2E 测试

**目标**：使用 Playwright 覆盖从登录到 RAG 问答的关键路径。

**涉及模块**：前端所有页面 + 后端 API

**建议实现方式**：
1. 安装 `playwright`，配置测试环境（本地 dev server + mock auth）
2. 编写以下测试场景：
   - 登录 → 进入主页
   - 创建生活记录 → 在时间线中查看
   - 上传文档 → 文档列表出现
   - 删除文档 → 文档列表消失
   - RAG 问答 → 返回回答和引用
   - 清除对话 → 对话历史清空

**验收标准**：
- [ ] 6 个核心流程有 Playwright E2E 测试
- [ ] 测试可在本地运行（`npx playwright test`）
- [ ] 测试通过率 100%

---

### 3.4 错误提示和空状态优化

**目标**：用户在异常场景下看到友好提示，而非空白页面或技术错误。

**涉及模块**：
- 前端所有页面的加载态、空态、错误态
- 后端错误响应格式统一

**建议实现方式**：
1. 为每个页面添加空状态组件（如"暂无记录"、"暂无文档"）
2. 为 API 错误添加统一的 toast 提示
3. 为网络错误添加重试机制
4. 确保 429 错误有特殊提示（"请求过于频繁"）

**验收标准**：
- [ ] 所有页面有空状态展示
- [ ] API 错误有友好提示（非白屏或技术错误码）
- [ ] 网络断开时有提示

---

### 3.5 RAG 引用准确性验证样例集

**目标**：建立一组标准问答对，量化 RAG 回答和引用的准确性。

**涉及模块**：
- `RagChatService`（问答逻辑）
- `CitationProcessor`（引文校验）

**建议实现方式**：
1. 准备 3-5 个测试文档（不同类型：PDF、TXT、MD）
2. 为每个文档编写 5-10 个标准问题和预期答案
3. 记录实际 RAG 回答，评估：
   - 回答是否正确（事实准确性）
   - 引用是否指向正确来源（引用准确性）
   - 引用是否为幻觉（不存在的引用）
4. 建立 `docs/rag_quality_benchmark.md` 记录评估结果

**验收标准**：
- [ ] 至少 20 个标准问答对
- [ ] 引用准确率 ≥ 80%（引用确实来自上传的文档）
- [ ] 幻觉引用率 ≤ 10%（引用的段落确实存在于文档中）
- [ ] 评估结果文档化

---

## 4. P2 任务：后续增强

### 4.1 管理后台 / 调试页面

**为什么不是现在**：当前用户只有开发者本人，管理后台的需求优先级低。Phase 4 引入 Agent 后，调试 Agent 执行过程的需求会更明确，届时再做更有针对性。

### 4.2 用户使用量统计

**为什么不是现在**：单用户场景下，使用量统计主要用于成本监控，GCP Billing 已足够。多用户场景（Phase 5+）才真正需要用户级别的使用量 dashboard。

### 4.3 文档处理状态更细粒度展示

**为什么不是现在**：当前文档处理是异步的（Cloud Tasks），状态只有"处理中"和"已处理"。更细粒度（提取中 → 分块中 → 嵌入中 → 完成）需要改造 worker 进度上报机制，工作量较大，当前体验可接受。

### 4.4 RAG 质量评分

**为什么不是现在**：需要先建立 3.5 的引用准确性样例集基线，才有数据支持评分系统的设计。Phase 4 引入 Agent 后，RAG 质量直接影响 Agent 输出质量，届时再系统化评分更有价值。

### 4.5 更完整的部署自动化

**为什么不是现在**：当前双服务（API + Web）手动部署流程已文档化（`docs/cloud-run-deploy-skill.md`），单人开发效率足够。CI/CD 流水线在 Phase 4 多人协作或频繁迭代时更有价值。

---

## 5. 推荐执行顺序

| 顺序 | 任务 | 优先级 | 预估工时 | 前置依赖 |
|------|------|--------|----------|----------|
| 1 | 检查现有安全和限制（文件大小、chunk 数、Firestore Rules） | P0 | 0.5 天 | 无 |
| 2 | 文件上传大小限制（后端补充） | P0 | 0.5 天 | 顺序 1 |
| 3 | 文档 chunk 数量限制 | P0 | 1 天 | 顺序 1 |
| 4 | Gemini / Embedding 成本保护（调用次数限制 + GCP 预算） | P0 | 1-2 天 | 顺序 1 |
| 5 | API Rate Limiting | P0 | 1-2 天 | 顺序 4（共享限流基础设施） |
| 6 | Firestore Security Rules 确认/补充 | P0 | 0.5 天 | 顺序 1 |
| 7 | 冒烟验证清单执行（首次完整执行） | P0 | 0.5 天 | 顺序 2-6 |
| 8 | 前端基础测试（Vitest） | P1 | 2-3 天 | 无（可与 P0 并行） |
| 9 | E2E 测试（Playwright） | P1 | 2-3 天 | 顺序 8 |
| 10 | RAG 引用准确性样例集 | P1 | 1-2 天 | 无（可与 P0 并行） |

**总预估工时**：P0 约 5-7 天，P1 约 5-8 天，总计约 2-3 周。

**并行建议**：
- 顺序 1-6 串行执行（安全和成本控制相互依赖）
- 顺序 8、10 可与 P0 并行（测试和质量验证独立于安全加固）
- 顺序 9 依赖顺序 8（E2E 测试需要前端测试基础设施就绪）

---

## 6. Phase 4 前置条件

进入 Phase 4 Agent 化 MVP 前，**至少需要满足**：

| 条件 | 说明 | 验证方式 |
|------|------|----------|
| P0 项基本完成 | 文件限制、chunk 限制、成本保护、限流、Firestore Rules 均已实施 | 本文档 P0 验收标准全部勾选 |
| 用户数据隔离确认 | Firestore Rules + 后端中间件双重防护 | 用两个不同账号测试跨租户访问被拒绝 |
| 成本风险可控 | GCP 预算告警已配置，限流已生效 | 故意高频调用触发 429，预算告警邮件可收到 |
| 核心链路可验证 | 冒烟清单全部通过 | 本文档 2.6 节全部勾选 |
| 部署流程稳定 | 能独立完成一次完整部署和验证 | 执行 `docs/cloud-run-deploy-skill.md` 流程无卡点 |
| RAG 引用可靠性有基本验证 | 至少完成引用准确性样例集初版 | 本文档 3.5 验收标准基本满足 |
| Phase 4 MVP 明确 | 清楚知道第一个做什么 | "每日总结 Agent / 生活复盘 Agent"（见 `docs/project-phase-assessment.md` 第 5 节） |

---

## 7. 不做事项

Phase 3.5 明确**不做**以下事项：

| 不做事项 | 原因 |
|----------|------|
| 不做复杂自主 Agent | 这是 Phase 4+ 的目标，Phase 3.5 只做稳定化 |
| 不做多工具 Agent | 同上，工具编排是 Phase 5 的内容 |
| 不做外部邮件/日历接入 | 外部集成是 Phase 5 的范围 |
| 不做长期记忆画像 | 用户画像和偏好学习是 Phase 6 的内容 |
| 不做自动后台任务调度 | 定时任务和主动触发是 Phase 4+ 的能力 |
| 不做未经用户确认的自动写入或执行 | 所有写入操作必须经过用户显式确认（Phase 4 也遵循此原则） |
| 不做新业务功能 | 稳定化阶段的边界：只加固，不扩展 |
| 不做大规模架构重构 | 当前架构满足 Phase 4 需求，无需重构 |

---

## 8. 文档维护说明

本文档在 Phase 3.5 执行过程中持续更新：
- 每完成一个 P0/P1 任务，更新对应验收标准的勾选状态
- 遇到新风险或阻塞时，在对应任务下添加备注
- Phase 3.5 全部完成后，更新 `docs/lifeos_project_roadmap.md` 中 Phase 3.5 的状态为 ✅

---

## 参考文档

| 文档 | 用途 |
|------|------|
| `docs/lifeos_project_roadmap.md` | 统一阶段定义（唯一基准） |
| `docs/project-phase-assessment.md` | 当前阶段评估和风险分析 |
| `docs/cloud-run-deploy-skill.md` | 部署流程文档 |
| `docs/phase3/phase3_non_goals.md` | Phase 3 非目标说明 |
| `CLAUDE.md` | 项目技术架构和开发指南 |

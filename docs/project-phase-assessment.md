# LifeAgent 项目阶段评估

> 评估日期：2026-06-30
> 评估依据：实际代码、API 端点、前端组件、测试文件、部署配置、docs/ 文档

---

## 1. 当前阶段结论

### 按项目文档定义

| 阶段 | 文档定义 | 完成状态 | 说明 |
|------|---------|----------|------|
| Phase 1 | 基础应用层 | ✅ 100% 完成 | Web + API + Auth + Firestore + Cloud Run |
| Phase 2 | 生活数据层 | ✅ 100% 完成 | 事件编辑/删除、提醒闭环、每日总结、数据迁移 |
| Phase 3 | 知识接入层（RAG） | ✅ 100% 完成 | 文档上传/处理管线、向量化、RAG 问答、引用验证、对话管理 |
| Phase 4 | Agent 化 MVP | ✅ 100% 完成 | Agent Preview + proposedAction + 确认状态机 + pending action |
| Phase 5 | Agent Write MVP | ✅ Development Complete | `create_life_event` feature-gated 写入路径，默认关闭 |
| Release Gate | 真实写入发布闸门 | 🟡 No-Go | canary / production enablement / gradual rollout，需单独批准 |
| Phase 6 | Memory Engine（长期记忆） | 🟡 当前开发阶段 | 长期记忆模型与能力待启动 |

### 按产品能力定义

当前属于 **Agent Write MVP Development Complete**，系统已具备 feature-gated `create_life_event` 写入路径，但生产真实写入仍关闭。

> **重要说明**：项目文档中的 "Phase 3" 定义为**知识接入层（RAG）**，而非 Agent 能力。
> `docs/phase3/phase3_non_goals.md` 明确将 "no agentic tool-use" 列为非目标。
> 因此，真正的 Agent 化应作为 **Phase 4** 规划。

**当前系统的本质**：一个已完成基础生活数据、RAG、Agent Preview 和第一个 Agent Write MVP 的 LifeAgent；真实写入上线属于 Release Gate，下一开发阶段是 Phase 6 Memory Engine。

---

## 2. 已完成能力清单

### Phase 1 — 生活记录 MVP

| 能力 | 后端 | 前端 | 测试 |
|------|------|------|------|
| 自由文本摄入（POST /api/life/ingest） | ✅ LifeEndpoints | ✅ IngestForm | ✅ LlmHelperTest |
| LLM 解析（Gemini + Mock） | ✅ GeminiLlmService / MockLlmService | — | ✅ 6 个 Theory 用例 |
| Schema 校验 | ✅ LifeEventSchemaValidator | — | ✅ LlmHelperTest |
| 事件列表（游标分页 + tag 过滤） | ✅ LifeEndpoints | ✅ Timeline | — |
| 事件详情 | ✅ LifeEndpoints | — | — |
| Firestore 多租户存储 | ✅ LifeEventService | — | ✅ 跨租户隔离测试 |
| Firebase Auth（Google + Mock） | ✅ FirebaseAuthMiddleware | ✅ AuthProvider | ✅ 401 测试 |
| Cloud Run 部署 | ✅ Dockerfile | ✅ deploy.sh | — |
| 自定义域名 | ✅ life.zhuchaofan.com | — | — |
| 异常处理中间件 | ✅ ExceptionMiddleware | — | — |

### Phase 2 — 智能助理

| 能力 | 后端 | 前端 | 测试 |
|------|------|------|------|
| 事件编辑（PUT /api/life/events/{id}） | ✅ LifeEndpoints | ✅ Timeline 内联编辑 | — |
| 事件软删除（DELETE） | ✅ SoftDeleteEventAsync | ✅ Timeline 删除按钮 | — |
| 数据迁移 API | ✅ MigrationEndpoints | — | — |
| 提醒意图检测（从摄入文本） | ✅ ParsedEvent.DetectedReminderIntent | — | ✅ LlmHelperTest |
| 提醒 CRUD（GET/PATCH） | ✅ ReminderEndpoints | ✅ ReminderWidget | ✅ ReminderServiceTest（7 个） |
| 提醒状态机（pending → completed/cancelled） | ✅ ValidateUpdate | ✅ 完成/取消按钮 | ✅ 8 个状态转换用例 |
| 每日总结生成 | ✅ DailySummaryEndpoints | ✅ DailySummaryCard | ✅ DailySummaryServiceTest（4 个） |
| 每日总结查询 | ✅ GET /api/daily-summaries/{date} | ✅ 自动查询今日总结 | — |
| AgentRun 日志 | ✅ AgentRun 模型 | — | — |
| 逾期提醒高亮 | ✅ DisplayStatus 计算 | ✅ 红色边框 + "已逾期" | — |

### Phase 3 — 知识接入层（RAG）

| 能力 | 后端 | 前端 | 测试 |
|------|------|------|------|
| 文档上传（PDF/TXT/MD → GCS） | ✅ DocumentEndpoints POST | ✅ KnowledgeBase 拖拽上传 | ✅ DocumentApiAndWorkerTest |
| 文档列表 | ✅ DocumentEndpoints GET | ✅ KnowledgeBase 文档表格 | ✅ 跨租户隔离测试 |
| 文档删除（级联：GCS + Firestore） | ✅ DocumentEndpoints DELETE | ✅ KnowledgeBase 二次确认删除 | ✅ 2 个删除测试 |
| 异步处理（Cloud Tasks → Worker） | ✅ InternalDocumentEndpoints | — | ✅ DocumentApiAndWorkerTest（4 个） |
| PDF 文本提取（PdfPig） | ✅ PdfPigDocumentTextExtractor | — | ✅ DocumentProcessingTest（3 个） |
| 文本分块（800 字 + 80 字 overlap） | ✅ BasicChunker | — | ✅ DocumentProcessingTest（2 个） |
| 文件校验（类型/大小/路径遍历防护） | ✅ FileValidator | ✅ 前端 10MB 限制 | ✅ DocumentProcessingTest（3 个） |
| Gemini Embedding（text-embedding-004, 768 维） | ✅ GeminiEmbeddingService | — | ✅ MockEmbeddingService 验证 |
| Firestore 向量存储（REST commit/runQuery） | ✅ RestFirestoreVectorStore | — | ✅ RestFirestoreVectorStoreTest（5 个） |
| RAG 问答（POST /api/v1/chat/rag） | ✅ RagChatService | ✅ RagChat 组件 | ✅ RagChatTest（17 个） |
| 引文脚标 + 来源显示 | ✅ CitationProcessor | ✅ Markdown.tsx 脚标 tooltip | ✅ 引文越界/缺失/部分测试 |
| 引文持久化（存储 + 加载） | ✅ ChatMessage.Citations | ✅ loadHistory 映射 citations | ✅ 历史加载测试 |
| 对话历史查询（GET） | ✅ GetRagChatHistoryAsync | ✅ 页面加载时自动查询 | ✅ 200/404/401 测试 |
| 清除对话（DELETE） | ✅ ClearRagChatHistoryAsync | ✅ 垃圾桶图标 + 确认弹窗 | ✅ 200/404/401 测试 |
| Markdown 渲染 | — | ✅ react-markdown + remark-gfm | — |
| 文档选择过滤搜索范围 | ✅ DocumentIds 参数 | ✅ RagChat 左侧 checkbox 面板 | ✅ 空结果测试 |
| OIDC 零信任（内部端点） | ✅ InternalDocumentEndpoints | — | ✅ userMismatch/gcsBypass 测试 |
| 嵌入维度异常检测 | ✅ RagChatService | — | ✅ 512 vs 768 维测试 |
| GCP 基础设施脚本 | ✅ setup-phase3-infra.sh | — | — |

### Phase 3.5 — 稳定化（非正式阶段，已吸收到后续阶段）

| 能力 | 状态 | 说明 |
|------|------|------|
| 移动端/窄屏适配 | ✅ | page.tsx 响应式布局（desktop tab + mobile stack） |
| 横向溢出修复 | ✅ | Markdown.tsx + RagChat.tsx 多处 overflow 修复 |
| 部署流程标准化文档 | ✅ | docs/cloud-run-deploy-skill.md |
| 后端单元测试 | ✅ | 41 个测试，覆盖 RAG、文档管道、提醒状态机、向量存储 |
| 前端测试 | ❌ | 仍需后续补齐 |
| E2E / 集成测试 | 🟡 | smoke 脚本已覆盖关键线上链路，完整 Playwright 仍未补齐 |
| Firestore Security Rules | 🟡 | 已有 rules 文件与 Phase 5.1 hardening 文档；真实写入上线前仍需 Release Gate 决策 |
| API Rate Limiting | ❌ | 无限流保护 |
| 错误监控/告警 | ❌ | 无 Sentry / Cloud Error Reporting |
| README.md | ❌ | 项目根目录缺少 README |

---

## 3. 当前风险点

### 🔴 高风险

| 风险 | 详情 | 建议缓解措施 |
|------|------|-------------|
| **Gemini API 成本无上限** | 每次 RAG 问答 = 1 次 embedding + 1 次 LLM（含大量上下文）；文档处理每 chunk = 1 次 embedding。无限流保护意味着恶意或误操作可能产生大量费用 | 添加 API Rate Limiting（每用户/每分钟）；设定月度成本预算警戒线 |

### 🟡 中风险

| 风险 | 详情 | 建议缓解措施 |
|------|------|-------------|
| **前端测试覆盖为零** | 7 个组件（IngestForm、Timeline、ReminderWidget、DailySummaryCard、KnowledgeBase、RagChat、Markdown）均无任何测试 | Phase 3.5 补充 Vitest + React Testing Library smoke test |
| **缺少 E2E / 集成测试** | 前后端交互、部署后功能验证完全依赖手动 | Phase 3.5 补充 Playwright 基础 E2E |
| **Firestore Security Rules 缺失** | 当前安全完全依赖后端中间件；如果直接访问 Firestore（如浏览器 console），无第二层防护 | 确认是否存在未提交的 rules 文件；如无，Phase 3.5 添加 |
| **RAG 准确性** | LLM 幻觉无法根除；`invalid_cleaned` 状态会丢弃幻觉引文但用户可能注意到缺失；嵌入质量取决于 Gemini 模型 | 持续监控 citationIntegrity 分布；考虑添加用户反馈机制 |

### 🟢 低风险

| 风险 | 详情 | 建议缓解措施 |
|------|------|-------------|
| **部署协调** | API 和 Web 需协调部署；`--source` 路径需与 Dockerfile 一致 | 已有 docs/cloud-run-deploy-skill.md |
| **双 GCP 项目复杂性** | Firestore (copper-affinity-467409-k7) vs Auth (my-agent-app-a5e42) 分离 | 已有配置和文档 |
| **硬编码常量** | `rag_default_session`（前端单会话）、`Asia/Shanghai`（时区） | 功能不受影响，后续多会话时再处理 |

---

## 4. 下一阶段建议

### Phase 6 — Memory Engine（建议下一开发阶段）

优先级排序：

| 优先级 | 任务 | 说明 |
|--------|------|------|
| P0 | Memory 数据模型 | 定义长期事实、偏好、目标、习惯、来源、审计字段 |
| P0 | Memory 权限边界 | `userId` 只能来自认证上下文，写入必须可审计 |
| P1 | Memory 提取与确认 | 从 life events / RAG / chat 中提取候选记忆，但先确认后保存 |
| P1 | Memory 检索 | Agent 在回答或建议前可读取相关长期记忆 |
| P2 | Memory 更新/删除 | 支持用户撤销、修改、清理长期记忆 |
| P2 | Release Gate 分离 | 真实写入 canary / 生产启用继续留在 Release Gate |

### Release Gate — 真实写入上线（非开发 Phase）

以下事项不属于 Phase 6 开发：

- 打开 `ENABLE_AGENT_WRITE_TOOLS`
- 打开 `ENABLE_CREATE_LIFE_EVENT_TOOL`
- 执行 real-write canary
- Production Enablement
- Gradual Rollout

Release Gate 的执行细节以 `docs/phase5_10_controlled_real_write_canary_runbook.md` 为准。

---

## 5. Phase 6 Memory Engine 初步定义

### 产品定位

**Memory Engine** — 从单次会话和单条生活记录，升级为可审计、可撤销、可检索的长期记忆系统。

### 核心流程

```
用户产生生活记录 / RAG 对话 / Agent 交互
  │
  ▼
Memory Engine 提取候选记忆
  ├─ 长期事实
  ├─ 偏好
  ├─ 目标
  ├─ 习惯
  └─ 历史状态变化
  │
  ▼
用户审阅 / 确认 / 修改 / 拒绝
  │
  ▼
保存长期记忆
  ├─ source / createdBy / agentActionId
  ├─ confidence / evidence
  ├─ createdAt / updatedAt
  └─ revoked / deleted lifecycle
  │
  ▼
Agent 在后续回答和建议中检索相关记忆
```

### 第一批能力

| 能力 | 功能 | 数据源 |
|------|------|--------|
| `propose_memory` | 从用户数据中生成候选长期记忆 | life_events / chat / RAG |
| `confirm_memory` | 用户确认后保存记忆 | Firestore memories |
| `search_memories` | 检索相关长期记忆 | Firestore memories |
| `update_memory` | 修正或合并已有记忆 | Firestore memories |
| `revoke_memory` | 撤销不准确或过期记忆 | Firestore memories |

### 明确不做（Phase 6 初期 Non-Goals）

- ❌ 不做未经用户确认的长期记忆写入
- ❌ 不把真实写入 canary 混入 Phase 6
- ❌ 不接日历、邮件、外部 MCP
- ❌ 不做主动后台自动化
- ❌ 不做跨用户/跨租户记忆

### 技术实现要点

1. **Memory 数据模型**：显式字段承载 `userId/source/createdBy/evidence/confidence/lifecycle`。
2. **权限边界**：`userId` 只能来自后端认证上下文。
3. **确认机制复用**：沿用 Phase 5 的 proposedAction / pending action / confirm 状态机。
4. **检索边界**：只读取当前用户的 memories，并保留引用来源。
5. **撤销能力**：用户必须能撤销或删除错误记忆。

### 预期产出

- Memory 数据模型和 service skeleton
- `propose_memory` preview-only flow
- 用户确认式 `save_memory`
- Memory 检索接入 Agent/RAG 上下文
- 记忆撤销/删除策略

---

## 6. 最终结论

### 明确判断

| 问题 | 结论 |
|------|------|
| Phase 1 是否已完成？ | ✅ **是**，全部功能已实现、测试、部署 |
| Phase 2 是否已完成？ | ✅ **是**，全部功能已实现、测试、部署 |
| Phase 3 是否已完成？ | ✅ **是**（文档定义的 RAG 阶段），全部功能已实现、测试、部署 |
| Phase 4 是否已完成？ | ✅ **是**，Agent Preview / 确认状态机 / pending action 已完成 |
| Phase 5 是否已完成？ | ✅ **Development Complete**，`create_life_event` Agent Write MVP 已完成 |
| 当前处于什么阶段？ | **Phase 6 Memory Engine**（开发阶段）+ **Release Gate No-Go**（发布闸门） |
| 是否可以开启真实写入？ | ❌ **不可以**，必须走 Release Gate 且再次显式批准 |
| Phase 6 第一步应该做什么？ | **Memory Engine 设计冻结** — 数据模型、权限边界、确认机制、撤销策略 |

### 一句话总结

> LifeAgent 已完成基础生活数据、RAG、Agent Preview 和第一个 Agent Write MVP 的开发闭环。
> 真实写入上线属于 Release Gate；下一开发阶段应进入 Phase 6 Memory Engine。

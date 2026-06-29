# Phase 4 Agent 化设计冻结与实施计划

> 创建日期：2026-06-29
> 当前前置状态：Phase 3 RAG MVP 已完成，Phase 3.5 自动化验证已完成；真实线上 Firebase 登录 E2E 仍需人工执行。
> 本文目标：冻结 Phase 4 的低风险设计边界和实施拆分。本文不包含业务实现代码。

## 当前实现状态

- Phase 4.0：已实现后端 preview skeleton，包括 `/api/agent/run`、`AgentRunner`、`ToolRegistry`、`ToolExecutor` 和 `IAgentTool`。
- Phase 4.1：已接入只读工具 `list_documents` 与 `get_document_status`。
- Phase 4.2：已实现 deterministic readonly planner；不执行写入工具。
- Phase 4.3A：已接入只读 RAG Agent 工具，`search_documents` 使用当前用户向量检索，`answer_with_rag` 复用现有 `RagChatService` 并保留 citations。
- 写入工具 `create_life_event` / `save_memory` / `create_reminder` 尚未实现，仍保留到 Phase 4.3 以后。

---

## 1. Phase 4 的目标定义

Phase 4 的目标是：在现有 RAG 文档问答、生活记录、提醒、每日总结基础上，引入一个**轻量、可控、可审计的单 Agent 执行层**。

Phase 4 要解决的问题：

- 让系统不再只执行固定 workflow，而是能根据用户意图选择是否调用后端工具。
- 将 RAG 问答、文档状态查询、生活数据读取等既有能力包装为受控工具。
- 支持 3～5 步以内的多步任务，例如“查看相关文档 → 汇总回答 → 给出可保存的生活记录建议”。
- 对所有工具调用进行日志记录，便于排查模型误调用、成本异常和权限问题。
- 对写入类动作引入用户确认，避免模型直接修改用户数据。

Phase 4 不追求完整自治 Agent。第一版应保持“用户触发、后端约束、可追踪、可中断、需确认”的产品形态。

---

## 2. 当前系统与 Agent 化的边界

### 当前 RAG Chat 已具备的能力

- 前端 `RagChat` 支持选择已处理成功的文档，并向 `/api/v1/chat/rag` 发起问答。
- `DocumentProvider` 已承担文档列表、上传后状态刷新、processing/deleting 轮询和跨组件共享状态。
- 后端 `RagChatService` 已完成 embedding、向量检索、文档 ID 后置过滤、历史消息拼接、LLM 回答、Citation 二次校验、消息持久化。
- `CitationNode` 与 `citationIntegrity` 已用于回答引用展示和历史恢复。
- 后端所有 RAG、文档、生活数据接口都从 `FirebaseAuthMiddleware` 注入的 `HttpContext.Items["userId"]` 获取用户身份，不信任前端传入 userId。

### 仍属于普通 workflow 的能力

- 文档上传：固定流程为上传 GCS → 写入 `KnowledgeDocument(status=processing)` → Cloud Tasks 异步处理。
- RAG 问答：固定流程为用户提问 → 检索 → 生成 → Citation 清洗 → 保存消息。
- 提醒和生活记录：当前由明确 API 和服务方法驱动，不由模型自主选择工具。
- 每日总结：属于固定入口的总结生成，不具备动态工具选择能力。

### 从什么功能开始算 Agent

Phase 4 中，只有满足以下条件才算 Agent 能力：

- 后端存在 `AgentRunner` 统一编排一次 Agent run。
- 模型只能在 `ToolRegistry` 中声明的工具内选择调用。
- 每次工具调用都经过 `ToolExecutor` 的输入校验、权限注入、风险判断和日志记录。
- Agent loop 有最大步数、超时和成本预算限制。
- 写入或高风险动作必须返回待确认状态，由用户确认后再执行。

### Phase 4 明确不做

- 不做复杂多 Agent 或角色协作框架。
- 不做自治后台任务、定时自动执行或无限循环。
- 不引入 MCP 作为 Phase 4 必需能力；MCP 仅作为 Phase 5 以后外部工具接入候选。
- 不迁移 Firebase Auth 项目，不修改 Firestore Rules，不调整 Cloud Run 生产配置。
- 不重构现有 RAG、文档上传、聊天主链路。
- 不允许模型决定 `userId`、权限字段、系统字段、Firestore 物理路径。
- 不允许前端直接访问 Firestore 作为 Agent 数据通道。

---

## 3. 建议的 Agent 架构

建议在 ASP.NET Core 后端中新增轻量 Agent 层，复用当前 DI、Minimal API、服务和仓储模式，不引入外部 Agent 框架。

### 核心组件

| 组件 | 职责 |
|---|---|
| `AgentRunner` | 执行一次 Agent run，控制最大循环次数、预算、超时、终止条件和最终响应 |
| `ToolRegistry` | 注册允许模型调用的工具，提供名称、描述、输入 schema、风险等级 |
| `ToolExecutor` | 负责工具参数校验、服务端注入 `userId`、执行工具、捕获错误、记录日志 |
| `IAgentTool` | 每个工具实现的统一接口 |
| `AgentRun` | 记录一次 Agent 执行的元数据、状态、耗时、错误摘要 |
| `ToolCallLog` | 记录每次工具调用的名称、参数摘要、结果摘要、耗时、状态 |
| `ConfirmationService` | 管理待确认写入动作，防止模型直接写入高风险数据 |

### 建议接口形状

```csharp
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    AgentToolRisk Risk { get; }
    bool RequiresConfirmation { get; }
    JsonElement InputSchema { get; }

    Task<AgentToolResult> ExecuteAsync(
        AgentContext context,
        JsonElement input,
        CancellationToken cancellationToken);
}
```

`AgentContext` 必须由后端构造，至少包含：

- `UserId`：来自 `FirebaseAuthMiddleware`，不接受模型或前端传入。
- `ConversationId`：来自当前聊天上下文，可沿用 `rag_default_session`，后续再扩展多会话。
- `ClientTimeZone`：来自请求，校验失败则回落到安全默认值。
- `SelectedDocumentIds`：来自前端选择，工具执行时仍需服务端校验文档归属。
- `RunId`：后端生成。
- `MaxToolCalls` / `CostBudget` / `Deadline`：执行控制参数。

### 风险等级

| 等级 | 示例 | 策略 |
|---|---|---|
| `read` | `list_documents`, `get_document_status`, `search_documents` | 可直接执行，但必须记录 |
| `compute` | `answer_with_rag` | 可直接执行，受 LLM/Embedding 配额限制 |
| `write` | `create_life_event`, `save_memory` | 必须先返回用户确认 |
| `external` | 日历、邮件、通知 | Phase 4 不实现 |

---

## 4. 第一批工具设计

Phase 4 第一批工具应以只读为主。可写工具只进入 4.3，并必须用户确认。

### `list_documents`

- 输入参数：`status?: "processing" | "success" | "failed" | "deleting" | "all"`, `limit?: number`
- 输出结构：`{ documents: [{ id, fileName, status, chunkCount, updatedAt }], hasMore?: boolean }`
- 权限边界：只能读取当前 `AgentContext.UserId` 下的 `users/{userId}/documents`。
- 失败处理：仓储异常返回 `tool_error`，Agent 不应改写为“没有文档”。
- 用户确认：不需要。

### `get_document_status`

- 输入参数：`documentId: string`
- 输出结构：`{ id, fileName, status, chunkCount, errorMessage?, updatedAt }`
- 权限边界：通过 `IDocumentRepository.GetAsync(userId, documentId)` 校验归属；不存在或越权统一返回 not found。
- 失败处理：not found 返回结构化错误；系统异常记录日志并返回失败。
- 用户确认：不需要。

### `search_documents`

- 输入参数：`query: string`, `documentIds?: string[]`, `topK?: number`
- 输出结构：`{ chunks: [{ documentId, documentName, chunkIndex, pageNumber, sectionTitle, snippetPreview, score }], citationSeeds: [...] }`
- 权限边界：embedding 和向量检索必须使用 `AgentContext.UserId`；`documentIds` 只能作为过滤条件，不能扩大权限。
- 失败处理：embedding 失败、维度异常、向量检索异常均返回 tool error；空结果返回 `chunks: []`。
- 用户确认：不需要。

### `answer_with_rag`

- 输入参数：`question: string`, `documentIds?: string[]`, `clientTimeZone?: string`
- 输出结构：`{ answer, citations: CitationNode[], citationIntegrity }`
- 权限边界：复用当前 `IRagChatService.ProcessChatAsync(userId, request)` 或抽取其内部只读检索/回答逻辑；userId 仍由后端注入。
- 失败处理：沿用当前 RAG 的拒答、LLM 异常和 Citation 清洗策略。
- 用户确认：不需要。
- 注意：若直接复用 `ProcessChatAsync`，它会保存 chat messages；Agent 模式需明确是否保存为同一会话消息，避免重复写入。

### `create_life_event` / `save_memory`

- 纳入条件：当前已有 `ILifeEventService.SaveEventAsync(userId, lifeEvent)`，具备按用户路径写入的基础。
- 输入参数：`content: string`, `occurredAt?: string`, `tags?: string[]`, `importance?: number`, `source?: "agent_suggested"`
- 输出结构：确认前返回 `{ proposedEvent: {...}, confirmationRequired: true }`；确认后返回 `{ eventId, saved: true }`
- 权限边界：`UserId`、`Id`、`CreatedAt`、`UpdatedAt`、Firestore 路径由服务端强制覆盖。
- 失败处理：schema 校验失败返回 validation error；写入失败返回 tool error。
- 用户确认：必须。

### `create_reminder`

- Phase 4 第一批不建议直接实现。当前 `IReminderService` 主要提供列表、读取、状态更新；提醒创建主要由 ingest 解析链路触发。
- 可作为 Phase 4.3 或 Phase 5 候选工具，前提是新增明确的后端创建服务，并保留状态机约束。
- 用户确认：必须。

---

## 5. Agent Loop 最小实现方案

建议最大循环次数为 3～5。默认 3，只有明确需要多步只读检索时提升到 5。

伪代码：

```text
POST /api/agent/run
  1. FirebaseAuthMiddleware 验签并注入 userId
  2. 创建 AgentContext(userId, conversationId, selectedDocumentIds, timezone, budgets)
  3. 创建 agent_run(status=running)
  4. for step in 1..maxSteps:
       a. 调用 LLM planner，输入用户消息、可用工具定义、已完成工具结果摘要
       b. 如果 planner 返回 final_answer:
            保存 run 状态为 completed
            返回 answer + citations + toolCalls
       c. 如果 planner 返回 tool_call:
            校验工具是否存在于 ToolRegistry
            校验参数 schema
            如果工具 RequiresConfirmation:
                保存 pending_confirmation
                保存 run 状态为 waiting_for_confirmation
                返回 needs_confirmation
            执行 ToolExecutor
            记录 tool_call 日志
            如果工具失败且不可恢复:
                保存 run 状态为 failed
                返回结构化错误
  5. 超过 maxSteps:
       保存 run 状态为 stopped
       返回“已达到步骤上限”的安全回答
```

硬约束：

- 每次工具调用必须记录，包括失败、参数校验失败、被确认机制拦截。
- 高风险动作只生成 proposal，不直接执行。
- 模型输出中的 `userId`、`tenantId`、Firestore path、权限字段一律忽略。
- 工具执行只能通过现有 service/repository，并传入后端认证得到的 `userId`。
- Agent endpoint 必须使用现有限流和每日配额策略，至少按高成本接口处理。

---

## 6. 数据结构影响评估

### 当前可复用结构

- `AgentRun` 模型已存在，路径设计为 `users/{userId}/agent_runs/{runId}`。
- `ChatSession` / `ChatMessage` 已能保存 RAG 对话和 Citation。
- `KnowledgeDocument` / chunks / `CitationNode` 已能支撑文档检索和引用展示。

### 是否需要新增集合

建议分阶段处理：

| 结构 | 是否 Phase 4 必须 | 建议 |
|---|---|---|
| `agent_runs` | 是，已有模型基础 | Phase 4.0/4.1 先复用并小幅扩展字段 |
| `tool_calls` | 只读 Agent 可先用日志；写工具前建议持久化 | Phase 4.2 前至少有结构化日志，4.3 前建议持久化 |
| `agent_messages` | 不是必须 | 优先复用现有 `chat_sessions/{id}/messages`，避免双聊天历史 |

### 最小 schema 建议

`agent_runs/{runId}` 最小字段：

- `userId`
- `conversationId`
- `taskType`
- `status`: `running | completed | failed | stopped | waiting_for_confirmation`
- `inputMessage`
- `finalAnswer`
- `citationIntegrity`
- `toolCallCount`
- `startedAt`
- `completedAt`
- `durationMs`
- `errorMessage`

`agent_runs/{runId}/tool_calls/{callId}` 最小字段：

- `step`
- `toolName`
- `risk`
- `status`: `success | failed | skipped | waiting_for_confirmation`
- `inputSummary`
- `outputSummary`
- `requiresConfirmation`
- `startedAt`
- `completedAt`
- `durationMs`
- `errorMessage`

迁移风险：

- Firestore schemaless，不需要传统数据库迁移。
- 风险主要在代码兼容：当前 `AgentRun` 字段偏每日总结场景，若直接复用需保持旧字段可空或默认值，不破坏 Daily Summary 现有读写。
- `tool_calls` 可以作为子集合逐步加入，不影响现有 RAG 和文档路径。

---

## 7. API 设计草案

### 推荐方案

新增 Agent 编排入口：

- `POST /api/agent/run`
- `GET /api/agent/runs/{runId}`

保留现有 RAG 入口：

- `POST /api/v1/chat/rag`
- `GET /api/v1/chat/rag/{conversationId}/messages`
- `DELETE /api/v1/chat/rag/{conversationId}/messages`

这样做的原因：

- 现有 RAG endpoint 是稳定能力，不应为了 Agent MVP 改坏主链路。
- Agent endpoint 是编排层，需要返回 `toolCalls`、`needs_confirmation`、`runId` 等新语义。
- 前端不应出现两个互相竞争的聊天页面；可以继续复用 `RagChat` 作为单一聊天界面，在 Agent 模式下调用 `/api/agent/run`。

### `POST /api/agent/run`

请求草案：

```json
{
  "conversationId": "rag_default_session",
  "message": "帮我基于这些文档总结训练计划，并保存一个复盘建议",
  "documentIds": ["doc_xxx"],
  "clientTimeZone": "Asia/Shanghai",
  "mode": "agent",
  "confirmation": {
    "confirmationId": "confirm_xxx",
    "decision": "approve"
  }
}
```

响应草案：

```json
{
  "runId": "run_xxx",
  "status": "completed",
  "message": "最终回答 Markdown",
  "citations": [],
  "citationIntegrity": "valid",
  "toolCalls": [
    {
      "step": 1,
      "toolName": "list_documents",
      "status": "success",
      "durationMs": 42
    }
  ],
  "pendingConfirmation": null
}
```

需要确认时：

```json
{
  "runId": "run_xxx",
  "status": "needs_confirmation",
  "message": "我可以保存以下生活记录，请确认。",
  "pendingConfirmation": {
    "confirmationId": "confirm_xxx",
    "toolName": "create_life_event",
    "proposal": {
      "content": "今日完成训练计划复盘...",
      "tags": ["review", "training"]
    }
  }
}
```

### `GET /api/agent/runs/{runId}`

用途：

- 查看 Agent 执行状态和工具调用日志。
- 调试前端超时、模型误调用、成本异常。
- 后续支持长任务时可作为状态查询接口。

---

## 8. 前端影响评估

Phase 4 前端应继续以 `RagChat` 为主，不新增复杂页面。

最小改动：

- 增加 Agent 模式入口或轻量开关，默认仍保持当前 RAG 问答行为。
- 在消息区展示工具调用过程，例如“正在检索文档”“正在生成回答”“等待确认保存”。
- 对 `needs_confirmation` 响应展示确认卡片，用户确认后再发起带 `confirmationId` 的请求。
- 对超时、429、工具失败、步骤上限停止展示明确错误。
- 继续使用现有 `Markdown` 与 `CitationNode` 渲染，Agent 的最终回答仍返回 `citations` 和 `citationIntegrity`。
- 继续依赖 `DocumentProvider` 获取文档状态和选择结果，不在 `RagChat` 内重复拉取文档列表。

不建议：

- 不新增独立 Agent 页面。
- 不在前端维护工具执行权限。
- 不让前端拼接 tool prompt。
- 不把工具状态和文档状态混成同一份状态；工具调用状态只属于当前消息/run。

---

## 9. 风险清单

| 风险 | 等级 | 规避方案 |
|---|---|---|
| 模型误调用工具 | P1 | 工具 allowlist、JSON schema 校验、风险等级、只读优先、写操作确认 |
| Agent 循环失控 | P0 | `maxSteps=3` 默认、最高 5、总超时、每 run 工具调用上限、达到上限安全停止 |
| 成本不可控 | P0 | Agent endpoint 使用 high-cost rate limit；LLM/Embedding 计入现有每日配额；限制 `topK` 和检索次数 |
| 引用丢失 | P1 | `answer_with_rag` 复用 CitationProcessor；Agent 最终响应保留 `CitationNode[]` 和 `citationIntegrity` |
| 用户数据越权 | P0 | userId 只来自 `FirebaseAuthMiddleware`；工具不接受 userId；所有 repository/service 调用传入后端 userId |
| 与现有 RAG 问答冲突 | P1 | 保留 `/api/v1/chat/rag`；Agent 作为新编排入口；前端仍单一聊天界面 |
| 前端状态复杂度上升 | P2 | 工具调用状态绑定到单条 assistant message/run；文档状态继续由 `DocumentProvider` 管理 |
| 写入动作误保存 | P1 | Phase 4.3 前不开放写工具；写工具只返回 proposal；用户确认后才执行 |
| Firestore Rules 跨项目问题未闭环 | P1 | Phase 4 不依赖前端直连 Firestore；继续通过后端鉴权隔离；Rules 部署仍作为独立安全债 |
| RAG 指定文档后置过滤导致召回不足 | P2 | Phase 4 不混入前置过滤改造；记录为检索质量优化项 |

---

## 10. Phase 4 分阶段实施计划

### Phase 4.0：设计和接口 skeleton

范围：

- 新增 Agent 相关接口和空实现 skeleton：`IAgentTool`、`ToolRegistry`、`ToolExecutor`、`AgentRunner`。
- 新增 API endpoint skeleton，但可以先返回 501 或 feature flag 关闭。
- 不接入真实 LLM planner，不执行工具，不改变现有 RAG 行为。

验收标准：

- 项目可编译。
- 现有 RAG、文档上传、登录行为不受影响。
- 新增接口没有绕过 Firebase 鉴权。

验证命令：

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
git diff --check
```

### Phase 4.1：实现 ToolRegistry + 只读工具

范围：

- 注册 `list_documents`、`get_document_status`。
- 可选实现 `search_documents`，但只返回检索片段，不生成最终回答。
- 单元测试覆盖工具不存在、参数错误、越权文档 ID、仓储异常。

验收标准：

- 工具只读，不产生 Firestore 写入。
- 工具执行必须使用认证 userId。
- 工具调用有结构化日志。

验证命令：

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
git diff --check
```

### Phase 4.2：接入 AgentRunner，但只允许只读工具

范围：

- 接入 LLM planner 或 mock planner。
- AgentRunner 支持最多 3 步只读工具调用。
- 最终回答可以调用 `answer_with_rag` 或由 planner 汇总只读结果。
- 不开放写工具。

验收标准：

- 超过最大步数时安全停止。
- 工具失败时返回可理解错误，不产生 500 泄漏。
- `citations` 和 `citationIntegrity` 仍可返回前端。
- 计入 high-cost rate limit 和每日配额。

验证命令：

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
```

### Phase 4.3：加入可写工具，但必须用户确认

范围：

- 实现 `create_life_event` 或 `save_memory`。
- 写工具第一次调用只生成 proposal 和 `confirmationId`。
- 用户确认后再次请求才调用 `ILifeEventService.SaveEventAsync`。
- `create_reminder` 仍建议延后，除非先补齐明确的 Reminder 创建服务。

验收标准：

- 未确认时绝不写入 Firestore。
- 确认 ID 不可跨用户使用。
- 模型无法覆盖 `userId`、`id`、`createdAt` 等系统字段。
- 取消确认会记录 skipped，不写入。

验证命令：

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
git diff --check
```

### Phase 4.4：前端展示工具调用过程

范围：

- `RagChat` 增加 Agent 模式下的工具调用状态展示。
- 支持 `needs_confirmation` 确认卡片。
- 错误、超时、429、步骤上限停止有明确 UI。
- 保持现有引用展示和 Markdown 渲染。

验收标准：

- 默认 RAG 模式无回归。
- Agent 模式不新增复杂页面。
- 移动端无横向溢出。
- 引用脚标和来源仍可显示。

验证命令：

```bash
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

### Phase 4.5：测试、安全和成本收口

范围：

- 增加 AgentRunner 单元测试、工具权限测试、确认流测试。
- 增加前端 Agent UI smoke test。
- 更新部署后手动 smoke checklist。
- 复核 Cloud Run env，确保生产不启用 mock auth。

验收标准：

- 只读 Agent、写工具确认、失败恢复、配额限制均有测试。
- 真实线上 E2E 通过后再标记 Phase 4 MVP 可用。
- 不新增 Firestore Rules 已完成结论，直到跨项目 Auth 闭环解决。

验证命令：

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
git diff --check
```

---

## 11. 是否建议立即开工

建议结论：

- 设计工作可以现在并行推进，包括本文档、接口草案、测试计划和 skeleton 评审。
- Phase 4.0 的纯 skeleton 可以在真实线上 E2E 前开始，但必须保证 feature flag 关闭或 endpoint 不改变现有行为。
- Phase 4.1 的只读工具可以在 E2E 前准备，但合入前应确认不影响现有 RAG、文档和登录链路。
- Phase 4.2 以后涉及真实 AgentRunner、LLM planner、前端入口或线上行为变化，建议等待 Phase 3.5 真实 Firebase 登录 E2E 通过后再进入主线开发。
- Phase 4.3 写工具必须等待真实 E2E 通过，并完成确认机制、权限测试和审计日志后再启用。

不建议立即做：

- 不应在真实 E2E 阻塞期间改动 Cloud Run env、Firestore Rules、Firebase Auth 项目。
- 不应把 RAG 指定文档过滤前置化混入 Phase 4 初始实现。
- 不应引入 MCP、多 Agent 框架或复杂后台调度。

---

## 结论

Phase 4 应从“单 Agent、少工具、只读优先、写入确认、可审计”开始。当前代码已经具备 RAG、文档状态、生活事件、提醒、每日总结、认证隔离和 `AgentRun` 模型基础，但还缺少统一工具注册、工具执行日志、Agent loop 控制和用户确认机制。

推荐下一步是先做 Phase 4.0 skeleton 评审，不进入真实 Agent 行为；等人工线上 E2E 通过后，再按 4.1～4.5 逐步开启只读工具、AgentRunner、确认式写工具和前端展示。

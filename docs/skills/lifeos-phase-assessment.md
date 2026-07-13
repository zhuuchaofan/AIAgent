# LifeOS Phase Assessment Skill

> 本文件定义了一个可复用的 Skill，用于统一判断 LifeOS / LifeAgent 的项目阶段。
> 当用户询问项目阶段、下一步方向、是否可以进入 Agent 化等问题时，AI 必须参考本 Skill。
> 本 Skill 的阶段定义优先级高于任何零散的 docs 文件。

---

## 1. Skill 目的

本 Skill 用于：

- 判断 LifeOS / LifeAgent 当前项目阶段
- 统一最终目标和阶段命名
- 回答"现在处于什么阶段""下一步做什么""是否可以进入 Agent 化"
- 避免把 RAG 阶段（Phase 3）和真正 Agent 化阶段（Phase 4）混淆
- 避免 AI 基于零散 docs 临场发挥或临时重新定义阶段

---

## 2. 使用场景

当用户提出以下问题时，应优先使用本 Skill：

- 现在项目到什么阶段了？
- Phase 1 / Phase 2 / Phase 3 完成了吗？
- 现在是不是 Agent？
- 接下来做什么？
- 能不能开始 Agent 化？
- docs 里的阶段定义是不是一致？
- 当前项目最终目标是什么？
- 项目共分几个阶段？
- 每一阶段目标是什么？
- 某个新需求应该归入哪个阶段？

---

## 3. 项目最终目标

LifeOS / LifeAgent 的最终目标不是单纯的聊天网站，也不是单纯的 RAG 应用，而是一个**个人生活 Agent 系统**。

最终形态应具备：

1. 用户登录和个人数据隔离
2. 生活记录、提醒事项、今日总结
3. 文档知识库和 RAG 问答
4. 长期记忆和用户偏好
5. 可控的 Agent 工作流
6. 能围绕用户目标生成建议、计划和行动项
7. 后续可接入日历、邮件、任务、外部工具
8. 在用户确认下执行保存、提醒、总结、计划等操作

**当前系统还不是完整 Agent，目前是 RAG MVP + 生活数据基础能力完成阶段。**

---

## 4. 统一阶段定义

以下阶段定义是项目内唯一标准，任何其他文档与此冲突时以本 Skill 为准。

### Phase 0：项目方向与架构设计

**目标**：
- 明确 LifeOS / LifeAgent 的产品方向
- 明确技术栈（.NET 10 Minimal API + Next.js 16 + Firestore + Gemini）
- 明确部署目标（GCP Cloud Run + 自定义域名）
- 明确阶段路线图

**完成标准**：
- 路线图文档存在
- API / Firestore / 前端基础设计文档存在
- Cloud Run 部署方向确定

---

### Phase 1：基础应用层

**目标**：
- 建立可访问的 Web 应用（Next.js 单页）
- 建立后端 API（.NET Minimal API + 端点分组）
- 接入登录 / Auth（Firebase Auth + Google Sign-In）
- 接入 Firestore（多租户 `users/{userId}/` 路径隔离）
- 支持 Cloud Run 部署（API + Web 双服务）
- 支持自定义域名访问（`life.zhuchaofan.com`）

**完成标准**：
- 用户可以访问线上站点并登录
- 前后端服务正常（`/health` 返回健康）
- 基础数据可以读写（POST 摄入 + GET 时间线）
- Cloud Run API / Web 均可独立部署

---

### Phase 2：生活数据层

**目标**：
- 支持个人记录的编辑和删除（PUT / DELETE）
- 支持提醒事项（自然语言意图识别 → `reminders` 集合 → 状态机）
- 支持今日总结（LLM 聚合 → `daily_summaries` → 缓存 / 重新生成）
- 支持数据迁移（Phase 1 存量数据补齐 `isDeleted` 字段）
- 建立用户级数据隔离结构（`users/{userId}/life_events|reminders|daily_summaries`）

**完成标准**：
- 用户可以创建 / 编辑 / 删除生活记录
- 今日总结和提醒事项在前端正常展示
- 提醒状态机完整（pending → completed / cancelled）
- 数据严格按用户隔离，无法越权访问

---

### Phase 3：知识接入层 / RAG

**目标**：
- 支持文档上传（PDF / TXT / MD → GCS → Cloud Tasks 异步处理）
- 支持文档列表、删除、选择
- 支持文档处理管线（文本提取 → 分块 → 768 维 Embedding → Firestore 向量存储）
- 支持 RAG 问答（向量检索 → 阈值过滤 → LLM 生成 → 引文校验）
- 支持引文脚标和引用来源（`CitationNode` + `citationIntegrity`）
- 支持 RAG 对话历史（持久化到 Firestore，页面加载时恢复）
- 支持清除当前 RAG 对话（前端状态 + 后端批量删除）
- 优化 Markdown 渲染和移动端展示

**完成标准**：
- 用户可以上传文档并基于文档进行问答
- 回答可以展示引文脚标和来源悬浮预览
- RAG 对话可持久化，刷新页面后历史记录和引文均恢复
- 当前对话可清除（不影响已上传文档）
- 页面在移动端可用，无横向滚动条
- 已通过后端单元测试（41 个）和线上部署验证

---

### Phase 3.5：稳定化、安全、测试、成本控制

**目标**：
- 在进入真正 Agent 化之前，补齐稳定性和安全基础
- 控制 LLM / Embedding 调用成本
- 增加关键链路测试覆盖
- 固化部署和线上验证流程
- **不新增业务功能，不大改现有架构**

**完成标准**：
- Firestore Security Rules 已确认（是否存在未提交文件，或需要新建）
- API Rate Limiting 有方案或实现（每用户 / 每分钟限制 LLM 调用）
- 文件上传大小有限制（前端 10MB + 后端校验，已确认存在）
- chunk 数量有限制（防止单文档产生过多 chunk 导致成本爆炸）
- Gemini / Embedding 成本有保护措施（月度预算警戒线或调用上限）
- 关键链路有冒烟验证清单（登录、上传、删除、RAG 问答、清除对话）
- 前端基础测试开始补齐（Vitest + React Testing Library）
- E2E 测试开始补齐（Playwright 基础流程）
- 部署流程文档化（`docs/cloud-run-deploy-skill.md` 已存在）

---

### Phase 4：Agent 化 MVP

**目标**：
- 从被动 RAG 问答升级为可控的 Agent 工作流
- 建立 Agent Preview
- 建立用户确认状态机
- 建立 pending action 持久化
- 为写入类动作提供 preview-only 的安全边界

**完成标准**：
- Agent Preview 可见
- 只读工具和 proposedAction 可用
- confirm / cancel / expired / idempotent lifecycle 可用
- pending action 持久化到 Firestore
- 默认不真实写入

---

### Phase 5：Agent Write MVP

**目标**：
- 完成第一个 Agent 写入工具 `create_life_event`
- 默认生产保持 preview-only
- 用 feature gate 控制真实写入路径
- 补齐幂等、失败保护、日志、smoke、回滚和 release gate 文档

**可能能力**：
- `create_life_event` proposedAction
- feature-gated confirm 写入路径
- `wroteData` / `previewOnly` / `createdResourceId` 返回语义
- structured logging
- preview-only deployment validation
- authenticated preview-only smoke

**完成标准**：
- Development Complete
- preview-only production smoke passed
- 真实写入 canary 被隔离到 Release Gate

---

### Release Gate：真实写入发布闸门

**目标**：
- 将真实写入 canary、生产启用和灰度发布从开发 Phase 中分离
- 执行前必须再次获得用户显式批准

**包含**：
- Real-write Canary
- Production Enablement
- Gradual Rollout

**完成标准**：
- dedicated test user 明确
- Cloud Run flags 显式开启并可回滚
- real-write smoke 只在批准后执行
- Firestore 写入、幂等、cleanup 和 rollback 均验证完成

**说明**：Release Gate 不属于任何开发 Phase。

---

### Phase 6：Memory Engine（长期记忆）

**目标**：
- 建立长期记忆模型
- 支持偏好、目标、习惯、长期事实和历史状态追踪
- 让 Agent 可在用户确认下沉淀、检索、更新长期记忆

**完成标准**：
- 有明确 memory 数据模型和权限边界
- 有 memory 提取、保存、更新、撤销/删除策略
- 长期记忆不由 LLM 或请求体决定 `userId`
- Agent 可读取相关记忆生成个性化建议

---

## 5. 当前阶段判断规则

以下状态是项目的唯一标准状态，任何其他文档与此冲突时以本 Skill 为准。

| 阶段 | 名称 | 状态 | 说明 |
|------|------|------|------|
| Phase 0 | 项目方向与架构设计 | ✅ 已完成 | 设计文档齐全 |
| Phase 1 | 基础应用层 | ✅ 已完成 | Web + API + Auth + Firestore + Cloud Run |
| Phase 2 | 生活数据层 | ✅ 已完成 | 记录 + 提醒 + 总结 + 数据迁移 |
| Phase 3 | 知识接入层 / RAG | ✅ 已完成 | 文档处理 + 向量化 + RAG 问答 + 引文校验 |
| Phase 3.5 | 稳定化、安全、测试 | ✅ 已吸收完成 | smoke、部署、观测、回滚等已在后续阶段补齐 |
| Phase 4 | Agent 化 MVP | ✅ 已完成 | Agent Preview + 确认状态机 + pending action |
| Phase 5 | Agent Write MVP | ✅ LifeEvent 最小写入已上线 | Unified Inbox 中 `life_record_preview` Confirm 后写入 `life_events` |
| Release Gate | 真实写入发布闸门 | 🟢 LifeEvent 最小闸门已通过；Memory Review 最小写入闸门已进入本地实现；其它写入 No-Go | Reminder、自动 Memory、Tool / 外部副作用仍未开放 |
| Phase 6 | Memory Engine（长期记忆） | 🟡 Review-confirmed minimal write 阶段 | Home AI 发现、Memory Review Inbox 与 RAG 只读上下文可展示候选线索；Review Inbox 状态可持久化；已留着候选可由用户明确点击“记住”后写入 durable Memory |

**一句话结论**：当前项目已完成 RAG、Agent Preview、pending action 持久化、LifeEvent 最小真实写入，并进入 Memory Review 确认写入阶段；当前收敛主线是 Unified Inbox + Memory Review，但自动 Memory write、Reminder write、Tool Execution 均未开放。

---

## 6. 命名规则

以后项目文档和 AI 回答中：

- **"Phase 1"** = 基础应用层（Web + API + Auth + Firestore + 部署）
- **"Phase 2"** = 生活数据层（记录 + 提醒 + 总结 + 迁移）
- **"Phase 3"** = 知识接入层 / RAG（文档处理 + 向量化 + RAG 问答）
- **"Phase 3.5"** = 稳定化、安全、测试、成本控制（非业务功能阶段）
- **"Phase 4"** = Agent 化 MVP（Agent Preview / 确认状态机 / pending action）
- **"Phase 5"** = Agent Write MVP（Unified Inbox life record Confirm -> `life_events` 已上线）
- **"Release Gate"** = 新真实写入目标的 canary / production enablement / gradual rollout（不属于开发 Phase）
- **"Phase 6"** = Memory Engine（长期记忆）
- **"Unified Inbox"** = 当前首页主线：AI/规则 intent classifier -> server-side pending action -> Confirm gate -> allowlisted executor

**命名约束**：

- ❌ 不再把 Agent 化称为 Phase 3
- ❌ 不再把当前阶段称为 Phase 2
- ❌ 不再把 RAG 阶段称为 Agent 化
- ❌ 不再把真实写入 canary 当作开发 Phase
- ✅ Phase 3 特指 RAG / 知识接入层
- ✅ Phase 4/5 已完成开发闭环，且 LifeEvent 最小写入已上线
- ✅ 当前收敛主线是 Unified Inbox；下一开发阶段是 Phase 6 Memory Engine

---

## 7. 回答格式要求

当用户询问项目阶段时，回答**必须**包含：

1. **当前阶段一句话结论**（例如：已完成 Phase 3（RAG），准备进入 Phase 3.5）
2. **按 Phase 0 到 Phase 6 的完成状态**（表格或列表）
3. **当前已经完成的核心能力**（按 Phase 分类，列出关键功能）
4. **距离真正 Agent 还缺什么**（明确列出 Phase 4 需要新增的能力）
5. **下一步建议**（Phase 3.5 或 Phase 4，取决于当前状态）
6. **是否建议现在开始 Phase 4**（给出前置条件）
7. **如果文档存在冲突，指出冲突并以本 Skill 为准**

回答时**不要只说 "Phase 3 已完成"**，必须补充说明：

> Phase 3 指 RAG / 知识接入层，不是真正 Agent 化。

---

## 8. 新需求归类规则

当用户提出一个新需求时，按以下规则判断应归入哪个阶段：

| 需求类型 | 归入阶段 | 示例 |
|----------|---------|------|
| 登录、部署、基础 API、Firestore 基础读写 | Phase 1 | "添加用户注册流程" |
| 记录、提醒事项、今日总结、会话消息 | Phase 2 | "支持编辑提醒的截止时间" |
| 文档上传、文档问答、embedding、引用来源、RAG 历史 | Phase 3 | "支持 DOCX 文件上传" |
| 测试、安全、限流、成本、部署流程、线上验证 | Phase 3.5 | "为 RAG 问答添加速率限制" |
| Unified Inbox intent 分类、pending action、life_events Confirm 写入 | Phase 5 / Unified Inbox | "输入后由 AI 判断生活记录还是提醒" |
| Agent 读取多个内部数据源并生成建议 | Phase 4/6 | "自动生成今日复盘报告" |
| 日历、邮件、任务、外部 API、多工具执行 | Release Gate + future Phase | "接入 Google Calendar 创建日程" |
| 长期画像、主动提醒、跨会话目标追踪 | Phase 6 | "根据过去 3 个月的数据识别用户习惯" |

如果需求跨多个阶段，优先归入最低适用阶段（即从 Phase 1 开始往上找第一个匹配的）。

---

## 9. 禁止事项

当执行项目阶段判断时，AI **不应**：

- 临时重新定义 Phase（例如把 Agent 化叫成 Phase 3.1）
- 把 RAG 阶段称为真正 Agent 阶段
- 把 Agent 化继续叫 Phase 3
- 忽略 `docs/lifeos_project_roadmap.md`
- 只基于 README 判断（项目根目录可能缺少 README）
- 在用户只要求阶段判断时修改业务代码
- 在阶段评估时自动部署
- 在阶段评估时自动提交（除非用户明确要求）

---

## 10. 参考文档

| 文档 | 用途 |
|------|------|
| `docs/lifeos_project_roadmap.md` | 统一项目总路线图（Phase 0-6），阶段判断唯一基准 |
| `docs/lifeos_unified_inbox_current_design.md` | 当前首页 Unified Inbox / pending action / LifeEvent 写入主线真源 |
| `docs/project-phase-assessment.md` | 2026-06-27 项目阶段评估，含已完成能力清单和风险分析 |
| `docs/cloud-run-deploy-skill.md` | Cloud Run 标准化部署流程 |
| `docs/phase3_5_stabilization_plan.md` | Phase 3.5 稳定化实施计划（如已创建） |

如果上述参考文档与本 Skill 的阶段定义冲突，**以本 Skill 为准**。

# LifeOS / LifeAgent — 统一项目路线图

> 本文档是判断项目阶段的**唯一基准**。
> 任何与本文档冲突的阶段定义，以本文档为准。

---

## 一、项目最终目标

LifeOS / LifeAgent 的最终目标不是单纯的聊天网站，也不是单纯的 RAG 应用，而是一个**个人生活 Agent 系统**。

最终形态应具备：

1. 用户登录和个人数据隔离
2. 生活记录、提醒事项、今日总结
3. 文档知识库和 RAG 问答
4. 可控的 Agent 工作流
5. 在用户确认下执行保存、提醒、总结、计划等操作
6. 长期记忆和用户偏好
7. 能围绕用户目标生成建议、计划和行动项
8. 后续可接入日历、邮件、任务、外部工具

**当前系统已具备 Unified Inbox 主线、pending action 持久化、AI/规则 intent classifier、LifeEvent 最小真实写入、带明确时间的 Reminder 确认写入，以及 Memory Review 中用户明确确认后的 durable Memory 写入。自动 Memory、提醒送达、Tool Execution 和外部副作用仍处于关闭状态。当前收敛主线见 `docs/lifeos_project_consolidation_map.md` 与 `docs/lifeos_unified_inbox_current_design.md`。**

---

## 二、统一阶段定义

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

**状态**：✅ 已完成（`docs/phase1/lifeosroadmap.md` 等设计文档）

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

**状态**：✅ 已完成

---

### Phase 2：生活数据层

**目标**：
- 支持个人记录的编辑和删除（PUT / DELETE）
- 支持提醒事项（自然语言意图识别 → `reminders` 集合 → 状态机）
- 支持今日总结（LLM 聚合 → `daily_summaries` → 缓存/重新生成）
- 支持数据迁移（Phase 1 存量数据补齐 `isDeleted` 字段）
- 建立用户级数据隔离结构（`users/{userId}/life_events|reminders|daily_summaries`）

**完成标准**：
- 用户可以创建 / 编辑 / 删除生活记录
- 今日总结和提醒事项在前端正常展示
- 提醒状态机完整（pending → completed / cancelled）
- 数据严格按用户隔离，无法越权访问

**状态**：✅ 已完成

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

**状态**：✅ 已完成

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

**状态**：✅ 已吸收到后续阶段（部署流程、smoke、观测、回滚与安全文档已在 Phase 4/5 中补齐）

---

### Phase 4：Agent 化 MVP

**目标**：
- 从被动 RAG 问答升级为可控的 Agent 工作流
- 第一批只做低风险、用户确认后保存的 Agent 能力
- 推荐第一个 MVP：**每日总结 Agent / 生活复盘 Agent**

**完成标准**：
- 用户可以点击"生成今日复盘"
- Agent 能读取当天记录、提醒事项、RAG 对话摘要
- Agent 能生成今日总结、明日建议、可执行提醒建议
- 用户确认后保存（不自动执行）
- 不做无限自主循环
- 不做未经用户确认的自动外部动作

**状态**：✅ 已完成（Agent Preview、确认状态机、持久化 pending action、`create_life_event` 写入基础设施）

---

### Phase 5：Agent Write MVP

**目标**：
- 完成第一个真实写入工具 `create_life_event` / `life_record_preview` 的可控写入链路
- 通过 Unified Inbox server-side pending action 和 Confirm Gate 控制真实写入能力
- 补齐确认状态机、幂等、失败保护、观测、smoke、回滚和 Release Gate 文档

**完成标准**：
- Unified Inbox 能生成 `life_record_preview` pending action
- Confirm 后仅 LifeEvent allowlisted executor 写入 `users/{userId}/life_events`
- 自动 Memory、提醒送达和 Tool Execution 不被该门禁打开
- 写入路径有代码、测试、审计标记和部署 smoke

**状态**：✅ LifeEvent 最小真实写入已上线

---

### Release Gate：真实写入发布闸门

**目标**：
- 将真实写入从开发阶段中分离出来，作为发布/运维决策处理
- 执行前必须再次获得显式批准
- 控制一次 dedicated test user 的真实写入 canary
- 验证 Firestore 写入、幂等、日志、rollback 和 cleanup
- 再决定是否进行 Production Enablement / Gradual Rollout

**包含**：
- Real-write Canary
- Production Enablement
- Gradual Rollout

**完成标准**：
- 用户明确批准 canary
- Cloud Run write flags 被临时开启并可回滚
- `RUN_AGENT_WRITE_SMOKE=true` 与 `EXPECT_AGENT_WRITE_ENABLED=true` 仅在 canary 中设置
- Firestore 中只创建明确标记的 `[SMOKE TEST]` 数据
- canary 后完成验证、回滚或继续灰度决策

**状态**：🟢 LifeEvent 最小闸门已通过；其它写入 No-Go

**说明**：Release Gate 不属于任何开发 Phase。LifeEvent、带明确时间的 Reminder 确认写入，以及 Memory Review 显式 remember 的最小闸门已推进；自动 Memory write、提醒送达、Tool Execution、MCP、外部系统写入、Cloud Run env 修改仍必须单独批准。

---

### Phase 6：Memory Engine（长期记忆）

**目标**：
- 建立长期记忆模型（偏好、目标、习惯、长期事实、历史状态追踪）
- 让 Agent 能在用户确认下沉淀、检索、更新长期记忆
- 支持基于长期记忆的个性化建议
- 为后续主动 LifeOS 打基础

**完成标准**：
- 有明确 memory 数据模型和权限边界
- 有 memory 提取、保存、更新、撤销/删除策略
- 长期记忆不由 LLM 或请求体决定 `userId`
- 用户可审阅或控制关键记忆写入
- Agent 可在 RAG / life event / reminder 上下文中读取相关记忆

**状态**：🟡 当前开发阶段

---

## 三、当前状态一览

| 阶段 | 名称 | 状态 | 说明 |
|------|------|------|------|
| Phase 0 | 项目方向与架构设计 | ✅ 已完成 | 设计文档齐全 |
| Phase 1 | 基础应用层 | ✅ 已完成 | Web + API + Auth + Firestore + Cloud Run |
| Phase 2 | 生活数据层 | ✅ 已完成 | 记录 + 提醒 + 总结 + 数据迁移 |
| Phase 3 | 知识接入层 / RAG | ✅ 已完成 | 文档处理 + 向量化 + RAG 问答 + 引文校验 |
| Phase 3.5 | 稳定化、安全、测试 | ✅ 已吸收完成 | smoke、部署、观测、回滚等已在后续阶段补齐 |
| Phase 4 | Agent 化 MVP | ✅ 已完成 | Agent Preview + 确认状态机 + pending action |
| Phase 5 | Agent Write MVP | ✅ LifeEvent 与 gated Reminder 最小写入已上线 | Unified Inbox Confirm 可写入 `life_events`；带明确时间的 Reminder 在独立闸门开启时可写入 `reminders` |
| Release Gate | 真实写入发布闸门 | 🟢 LifeEvent、Reminder 与显式 Memory remember 最小闸门已推进；其它写入 No-Go | 提醒送达、自动 Memory、Tool / 外部副作用仍关闭 |
| Phase 6 | Memory Engine（长期记忆） | 🟡 Memory Value Loop + 个性化今日关注 | 用户可明确确认、查看、编辑和归档 durable Memory；Memory Review 会区分稳定、观察、一次性和已有相近记忆线索；首页以只读方式结合 Memory、提醒、近期模式和待判断线索生成今日关注与个人化今日简报；最近回顾可展示只读记忆/计划依据；自动 Memory write 未启用 |

**一句话结论**：当前项目已完成 RAG、Agent Preview、pending action 持久化和多条用户确认后的最小写入闭环；当前处于 Phase 6 Memory Value Loop + 个性化今日关注阶段。

---

## 四、统一命名规则

以后项目文档中：

- **"Phase 1"** = 基础应用层（Web + API + Auth + Firestore + 部署）
- **"Phase 2"** = 生活数据层（记录 + 提醒 + 总结 + 迁移）
- **"Phase 3"** = 知识接入层 / RAG（文档处理 + 向量化 + RAG 问答）
- **"Phase 3.5"** = 稳定化、安全、测试、成本控制（非业务功能阶段）
- **"Phase 4"** = Agent 化 MVP（Agent Preview / 确认状态机 / pending action）
- **"Phase 5"** = Agent Write MVP（Unified Inbox life record Confirm -> `life_events` 已上线）
- **"Release Gate"** = 新真实写入目标的 canary / production enablement / gradual rollout（不属于开发 Phase）
- **"Phase 6"** = Memory Engine（长期记忆）
- **"Unified Inbox"** = 当前首页主线：intent classifier -> server-side pending action -> Confirm gate -> allowlisted executor

**命名约束**：
- ❌ 不再把 Agent 化称为 Phase 3
- ❌ 不再把当前阶段称为 Phase 2
- ❌ 不再把真实写入 canary 当作开发 Phase
- ✅ Phase 3 特指 RAG / 知识接入层
- ✅ Phase 4/5 已完成开发闭环，且 LifeEvent 最小写入已上线
- ✅ 当前收敛主线是 Unified Inbox；Phase 6 是下一开发阶段

---

## 五、阶段命名冲突说明

在整理过程中发现 `docs/phase1/lifeosroadmap.md`（原始路线图）与后续实际实施之间存在阶段定义冲突：

### 冲突 1：原始 6 周计划 vs 实际 3+1 阶段划分

**原始文档**（`docs/phase1/lifeosroadmap.md`）定义的是**按周迭代**的 6 周计划：

| 周次 | 原始定义 | 实际落地情况 |
|------|---------|-------------|
| 第 1 周 | 生活记录 MVP | → 对应 Phase 1 ✅ |
| 第 2 周 | 可查询记忆系统 | → 合并进 Phase 1 ✅ |
| 第 3 周 | 提醒系统基础版 | → 对应 Phase 2 的 2B（Reminder）✅ |
| 第 4 周 | 轻量 Agent 闭环 | → 未独立实施，Agent 能力推迟到 Phase 4 |
| 第 5 周 | 模块化 Dashboard | → 未独立实施，各模块已融入主页面 |
| 第 6 周 | 周期性总结 | → 对应 Phase 2 的 2C（Daily Summary）✅ |

**说明**：原始路线图是一个初步的按周规划，实际实施时调整为**按功能领域划分**的 Phase 1/2/3 结构。这是正常的产品迭代，不是错误。

### 冲突 2：Phase 3 non-goals 中提到 "Phase 4"

**文件**：`docs/phase3/phase3_non_goals.md` 第 55 行

> "动态决策、真正的智能 Agent Tool-Use 将作为 Phase 4 的核心突破口。"

**说明**：此表述与本文档的 Phase 4 定义（Agent 化 MVP）一致，不构成冲突。

### 冲突 3：`project-phase-assessment.md` 与本文档

**文件**：`docs/project-phase-assessment.md`

**说明**：该评估文档是阶段性历史评估。当前阶段定义以本文档、`docs/skills/lifeos-phase-assessment.md` 和 `docs/lifeos_project_consolidation_map.md` 为准：Phase 5 LifeEvent 最小写入已上线，Reminder / Memory / Tool 等新写入目标仍为 No-Go。

### 后续修正建议

- `docs/phase1/lifeosroadmap.md` 的"6 周计划"表保留作为历史记录，但不再作为阶段判断依据
- 后续新增文档一律使用本文档定义的 Phase 0-6 命名
- 如需引用旧阶段名称，在括号中注明对应的新阶段，例如：*"原路线图第 4 周（对应 Phase 4）"*

---

## 六、参考文档索引

| 文档 | 用途 | 是否与本文档一致 |
|------|------|----------------|
| `docs/lifeos_project_consolidation_map.md` | 当前阶段、代码、文档、线上状态总索引 | ✅ 当前真源 |
| `docs/lifeos_unified_inbox_current_design.md` | Unified Inbox / pending action / LifeEvent 写入主线设计 | ✅ 当前真源 |
| `docs/phase1/lifeosroadmap.md` | Phase 1 初始设计（含 6 周计划） | ⚠️ 周计划与实际阶段划分不同（见冲突 1） |
| `docs/phase1/phase_1_review.md` | Phase 1 收尾检查 | ✅ |
| `docs/phase1/phase1_mvp_tasks.md` | Phase 1 任务列表 | ✅ |
| `docs/phase2/lifeos_phase2_roadmap.md` | Phase 2 路线图 | ✅ |
| `docs/phase2/phase2_mvp_tasks.md` | Phase 2 任务列表 | ✅ |
| `docs/phase3/lifeos_phase3_roadmap.md` | Phase 3 路线图 | ✅ |
| `docs/phase3/phase3_non_goals.md` | Phase 3 非目标（含 Phase 4 引用） | ✅ |
| `docs/phase3/phase3_mvp_tasks.md` | Phase 3 任务列表 | ✅ |
| `docs/project-phase-assessment.md` | 项目阶段评估（2026-06-27） | ✅ |
| `docs/cloud-run-deploy-skill.md` | 部署流程文档 | ✅ |

---

## 附录：技术架构简图

```
浏览器 (Next.js 16, React 19)
  │  Firebase Auth (Google Sign-In)
  │  Cookie → Authorization: Bearer
  ▼
LifeAgent.Api (.NET 10 Minimal API)
  │  FirebaseAuthMiddleware
  │  ExceptionMiddleware
  ├─ LifeEndpoints / ReminderEndpoints / DailySummaryEndpoints
  ├─ DocumentEndpoints / InternalDocumentEndpoints
  ├─ RagChatEndpoints / MigrationEndpoints
  │
  ├─ Firestore (多租户: users/{userId}/...)
  │    ├─ life_events, reminders, daily_summaries, agent_runs
  │    ├─ documents, chunks (向量 768 维)
  │    └─ chat_sessions/{id}/messages
  ├─ Cloud Storage (文档原文件)
  ├─ Cloud Tasks (异步文档处理)
  └─ Gemini API (LLM 解析 / Embedding / RAG 生成)

GCP 项目:
  ├─ copper-affinity-467409-k7 (Firestore, GCS, Cloud Tasks)
  └─ my-agent-app-a5e42 (Firebase Auth)

部署:
  ├─ life-agent-api → Cloud Run (us-central1)
  ├─ life-agent-web → Cloud Run (us-central1)
  └─ life.zhuchaofan.com → 自定义域名
```

# Phase 2 AI 开发者行动指南 (AI Developer Instructions)

> [!IMPORTANT]
> **本文件专门提供给 AI 编码助手（如 Antigravity）阅读。**
> AI 开发工具在执行 Phase 2 开发任务时，必须百分之百遵守本指南中的所有指令，严禁自行扩展范围、盲目修改或跳过步骤。

---

## 1. 严格的执行顺序与提交流程
*   **按部就班开发**：必须严格遵循 [phase2_execution_order.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase2_execution_order.md) 中规划的顺序，串行执行。**绝对不允许**一次性把所有阶段（2A-0, 2A, 2B, 2C）的代码全部写完再提交。
*   **小步提交（Clean Commit）**：每开发完成其中的一个小步骤（例如任务 2A-0 迁移开发），必须保证本地通过编译、无 Lint 报错，并在本地执行一次 Git 提交（Commit）。
*   **开发验证双轮驱动**：每完成一个子阶段的 API 开发，必须使用 [phase2_verification_plan.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase2_verification_plan.md) 中对应的验证 `curl` 命令在本地进行手工调用校验，确认无误后才能宣布进入下一阶段。

## 2. 严禁扩展范围 (Anti-Scope Creep)
*   **严禁开发 Non-Goals**：AI 在编写代码时，必须仔细核对 [phase2_non_goals.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase2_non_goals.md) 中的所有排他特性（如 Telegram 推送、Scheduler 定时自动总结、图片语音多模态输入、长文本 RAG 会话历史、家庭共享组等）。**绝对不允许**在代码中偷偷实现这些非目标功能。
*   **克制代码膨胀**：如果 AI 觉得某些功能“很酷”或“未来会用到”，请将其写入 Roadmap 延伸讨论，禁止直接写入当前阶段的 PR 中。

## 3. 安全防范与多用户隔离底线
*   **信任零边界**：所有受保护接口必须提取解析 Firebase ID Token。**严禁信任**前端 Request Body 或 Query 中传入的 `userId` 属性。
*   **子集合物理隔离**：所有针对 `life_events`、`reminders`、`daily_summaries`、`agent_runs` 集合的物理读写与查询操作，必须强制走子集合路径 `users/{uid}/...`。
*   **越权防御**：在对特定资源 ID 执行 `PUT`, `PATCH`, `DELETE` 时，若根据 ID 在当前登录用户的子集合内检索不到该文档，**必须直接返回 `404 Not Found`**（或 `403 Forbidden`），绝不泄露其他用户的数据存在性，防止越权 ID 遍历攻击。
*   **已删除文档编辑拦截**：在 `PUT` 修改事件时，若该事件文档的 `isDeleted == true`，**必须直接返回 `404 Not Found`**（或 `400 Bad Request`），拦截任何已删除事件的修改，防止前端缓存导致的事件“半复活”漏洞。

## 4. 迁移接口的安全生命周期
*   **迁移安全准入**：`/api/migration/run-phase2a0` 迁移接口必须在 Header 中强校验 `X-Migration-Secret`，该密钥必须与后端环境变量 `MIGRATION_SECRET` 严格匹配。
*   **及时下线原则**：迁移成功验证完毕后，必须物理删除此迁移 Controller 路由文件，或在配置中通过 `ENABLE_MIGRATION_API=false` 彻底关闭该路由。不允许此接口在生产环境长期在线。

## 5. 原子性双写与事务规范
*   **双写事务化**：在 Ingest 阶段，如果确定需要创建 Reminder，向数据库写入 `LifeEvent` 和 `Reminder` 时，**必须使用 Firestore WriteBatch 或 Transaction 组合成一次原子提交**。
*   保证 `LifeEvent.createdReminderId` 与 `Reminder.sourceEventId` 双向关联的强一致性，防止半成功写入（如仅 Event 成功而 Reminder 缺失）。

## 6. LLM JSON 解析与空状态容错
*   **复用 JSON 清洗器**：Ingest 服务和 Daily Summary 服务在提取 LLM 的 JSON 响应时，**必须复用统一实现的公共 Helper 方法 `ExtractJsonObject(string raw)`**。该方法需实现：剥离 ```json 包裹标记，以及通过匹配第一个 `{` 到最后一个 `}` 截取真正 JSON。
*   **无记录日拦截**：Daily Summary 服务在捞取指定自然日数据后，若发现 `LifeEvent` 事件数量为 0，**必须直接在后端拦截，绝不调用大语言模型（LLM）**。此时直接构建返回硬编码的空状态总结（summary = "这一天还没有记录。"，highlights = []，moodLabel = "暂无记录"，moodScore = null，suggestions = []），绝不让模型无中生有。

## 7. 部署与调试原则
*   **本地优先调试**：AI 在修改 Bug 时，只允许在本地 Docker 或本地宿主机环境下跑通和编译，绝对不允许把未经验证的代码直接推送到 Main 分支触发 CI/CD 并在 Cloud Run 生产环境反复试错。
*   **出错立即回滚**：若发布新版本后生产环境报错率飙升或出现阻断性故障，必须优先通过 `gcloud run services update-traffic` 将流量回滚至上一个稳定 revision，然后在本地彻底定位修复后再重新发起部署。

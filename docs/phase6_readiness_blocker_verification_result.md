# Phase 6 Readiness Blockers 线上验证结果

本文档记录了对最新 API 版本执行线上部署以及 preview-only 状态下的完整冒烟回归验证结果。

## 1. 验证元数据
- **执行时间**：2026-06-30 22:08 (UTC+8)
- **部署前 Git 状态**：`main...origin/main [ahead 3]`, `working tree clean`
- **部署前测试结果**：通过，共 221 个单元测试全部成功 (`Passed: 221`)
- **旧 API Revision**：`life-agent-api-00035-tnf`
- **新 API Revision**：`life-agent-api-00036-jlh`
- **Web Revision 状态**：未发生任何变化，保持为 `life-agent-web-00018-bpq`

---

## 2. 部署前后环境变量核对

通过以下命令进行了只读检查：
`gcloud run services describe life-agent-api --region us-central1 --project copper-affinity-467409-k7`

部署前与部署后的环境变量设置完全一致：
- `USE_MOCK_AUTH` = `false`
- `USE_MOCK_LLM` = `false`
- `ENABLE_AGENT_WRITE_TOOLS` = 未设置 (默认为 `false`)
- `ENABLE_CREATE_LIFE_EVENT_TOOL` = 未设置 (默认为 `false`)

**结论**：写开关安全网依旧保持完全关闭，无任何被误启用的真实写入风险。

---

## 3. 冒烟测试执行结果

在配置了有效的 `FIREBASE_ID_TOKEN` 后，我们在新发布的 API revision 上重跑了完整的冒烟测试，结果如下：

### 3.1 smoke-agent-life-event-write 结果
- **公共健康监测**：`PASS API /health returns healthy`
- **Agent 规划行为（Planner Behavior）**：`PASS Agent proposes life_event action`
  - 成功触发 `create_life_event` (或 `create_life_event_preview`) 动作。
  - `requiresConfirmation` 返回 `true`。
- **确认流与写入控制验证**：`PASS Confirm action and verify expected write mode`
  - 返回值中 `previewOnly` = `true` 且 `wroteData` = `false`。
  - `createdResourceId` 为空，数据库未创建任何真实的 `life_event` 数据。
- **幂等性验证**：`PASS Repeat confirm and verify idempotency`

### 3.2 smoke-rag-e2e 结果
- **公共健康监测**：`PASS API /health returns healthy`
- **API 根路径测试**：`PASS API endpoint responds`
- **Web 根路径测试**：`PASS Web endpoint is reachable`
- **文档列表获取**：`PASS Agent Preview lists documents`
- **提醒规划预览**：`PASS Agent Preview proposes reminder confirmation`
- **增删改沙箱流**：`SKIP Authenticated upload/RAG/delete flow` (未配置 `RUN_MUTATING_SMOKE=true`，安全跳过，符合预期)

---

## 4. 核心校验结论

- **是否出现 wroteData=true**：否。
- **是否在数据库创建了 life_event**：否。
- **是否仍未启用真实写入**：是，依然完全关闭。
- **Phase 6 readiness smoke blockers**：**已清除 (Cleared)**。在最新 API 部署版本（revision 36）上，已成功在真实认证流量下验证了 preview-only 状态机与 Planner behavior，性能稳定。
- **Phase 6 implementation 状态**：由于真实写入 canary 被隔离在 Release Gate 且依然为 No-Go，真实数据写入依然处于 Block 状态，但开发的前置 blocker 已完全清除。

## 5. 最终结论与后续建议

**Phase 6 readiness smoke blockers cleared, but real writes remain disabled.**

基于部署与线上冒烟的完美表现，我们**建议进入 Phase 6.1 的设计安全实现阶段**（design-safe implementation）。即可以开始构建 Memory Engine 相关的 Model、Schema、Validator 和 Fake Repository（进行内存及单元测试逻辑实现），但仍**不接入真实 Firestore 物理库、不暴露新的写 API、不修改生产部署环境变量**。

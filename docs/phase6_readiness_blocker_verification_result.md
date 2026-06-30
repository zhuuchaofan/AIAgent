# Phase 6 Readiness Blockers 线上验证结果

本文档记录了对最新 API 版本执行线上部署以及 preview-only 状态下的冒烟回归验证结果。

## 1. 验证元数据
- **执行时间**：2026-06-30 22:00 (UTC+8)
- **部署前 Git 状态**：`main...origin/main [ahead 2]`, `working tree clean` (无任何未提交改动)
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

### 3.1 smoke-agent-life-event-write 结果
由于当前环境未设置 `FIREBASE_ID_TOKEN`，测试流程如下：
- **公共健康监测**：`PASS API /health returns healthy`
- **认证测试路径**：`SKIP Authenticated Agent flow: FIREBASE_ID_TOKEN is not set.`
- **写入行为审计**：未发生 `wroteData=true`，未创建任何 `life_event` 实体，安全防护有效。

### 3.2 smoke-rag-e2e 结果
由于当前环境未设置 `FIREBASE_ID_TOKEN`，测试流程如下：
- **API 健康监测**：`PASS API /health returns healthy`
- **API 根路径测试**：`PASS API endpoint responds`
- **Web 根路径测试**：`PASS Web endpoint is reachable`
- **RAG/Agent 认证流**：`SKIP Authenticated RAG and Agent Preview flows: FIREBASE_ID_TOKEN is not set.`

---

## 4. 核心校验结论

- **是否出现 wroteData=true**：否。
- **是否在数据库创建了 life_event**：否。
- **是否启用真实写入**：否。
- **是否解除 Phase 6 implementation blocker**：**否**。由于缺少有效的认证 Token，无法在线上对最新 revision 的 authenticated 意图规划及 preview-only 行为进行真实回归验证。

## 5. 最终结论与后续建议

**Phase 6 implementation remains blocked.**

由于缺少 `FIREBASE_ID_TOKEN` 导致未能对最新 revision 的真实 planner 表现完成全面验证。目前系统在 preview-only 状态下的只读接口表现稳定，后续需在获取有效 Token 后重跑此 readiness smoke，待 blockers 彻底清除后再进入 Phase 6.1 (模型与仓储实现) 的开发。

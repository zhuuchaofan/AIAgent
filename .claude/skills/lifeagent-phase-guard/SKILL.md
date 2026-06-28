# lifeagent-phase-guard

## Skill 定位

本 Skill 用于 LifeAgent / LifeOS 项目的**阶段管理、稳定化检查、Release Guard、P0 任务收尾和部署验证**。不用于自动开发业务功能。

### 适用场景

- 判断当前项目处于哪个 Phase / P0
- 判断当前任务是否完成
- 生成下一步执行建议
- 做 git 状态检查
- 做 diff review
- 做提交前自检
- 做部署前检查
- 做部署后验证
- 做关键链路冒烟验证
- 维护 Phase 文档
- 防止误部署、误提交、误 push、误执行高成本线上测试

---

## 项目背景

### 基本信息

| 项 | 值 |
|---|---|
| 项目名 | LifeAgent / LifeOS |
| 技术栈 | .NET 10 Minimal API, Cloud Run, Firebase Auth, Firestore, RAG, PDF 上传, chunk, embedding, LLM 问答 |
| 前端 | Next.js 16, React 19, Firebase Auth (Google Sign-In) |
| 后端 | LifeAgent.Api (C# .NET 10) |
| 基础设施 | GCP Cloud Run, Cloud Tasks, Cloud Storage, Firestore |

### 当前阶段

**Phase 3.5 稳定化** — 不新增业务功能，只做安全加固、成本控制、测试覆盖、部署稳定性。

> ⚠️ **快照说明**：以下 commit hash、Cloud Run revision 为 Skill 创建时（2026-06-28）的项目快照。后续执行时**必须以 `git log`、`gcloud run services describe` 和 `docs/` 最新内容为准**，不要把旧 revision / commit 当成永久事实。

### 已完成内容

| P0 任务 | 状态 | 提交 | 部署 |
|---|---|---|---|
| 2.1 Firestore Security Rules | ✅ 文件已创建 | `2caca6e` | ⏸️ 未部署（跨项目 Auth 阻塞） |
| 2.2 API Rate Limiting | ✅ 完成 | `39be375` | ✅ revision 00028-66b |
| 2.3 文件上传大小限制 | ✅ 完成 | `275cc3b` | ✅ |
| 2.4 文档 chunk 数量限制 | ✅ 完成 | `4e0cfb7` | ✅ |
| 2.5 Gemini/Embedding 成本保护 | ✅ 完成 | `275cc3b` | ✅ |
| 2.6 关键链路冒烟验证清单 | ✅ 完成 | `7366d47` | — |

### 重要风险

| 风险 | 说明 |
|---|---|
| **跨项目 Auth** | Firestore 项目：`copper-affinity-467409-k7`，Firebase Auth 项目：`my-agent-app-a5e42` |
| **Rules 部署阻塞** | 在这两个项目未统一前，**禁止部署 Firestore Rules** |
| **firebase deploy** | **禁止执行**，除非明确确认 Auth / Firestore 项目问题已解决 |

---

## 流程原则

### 执行纪律

1. **提交、push、部署、新 P0 必须拆开执行** — 不要把多个阶段动作混在一句命令里
2. **未 review diff 前不要提交**
3. **未确认 git clean 前不要部署**
4. **未确认 Cloud Run revision / traffic / health 前不要认为部署成功**
5. **任何阶段完成前必须输出**：是否可以提交、是否可以 push、是否可以部署、是否可以进入下一阶段

### 禁止事项

- ❌ 不要自动部署 Firestore Rules
- ❌ 不要执行 `firebase deploy`
- ❌ 不要线上压测
- ❌ 不要用大文件刷线上
- ❌ 不要用大量请求刷 Rate Limit
- ❌ 不要大量触发 embedding / LLM 调用
- ❌ 高成本链路默认只生成验证清单，不直接在线上执行
- ❌ 不要在用户未明确要求时继续下一个 P0

### 安全边界

- 提交前必须 `git diff` review
- 部署前必须 `git status --short` 确认干净
- 部署后必须确认 revision、traffic、health
- 每个子任务完成后**停止等待用户确认**，不自动推进

---

## 标准检查命令

### Git 检查

```bash
git status --short
git log --oneline -5
git diff --stat
git diff
git rev-parse --short HEAD
```

### 测试

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --verbosity minimal
```

### Cloud Run 状态

```bash
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="value(status.url,status.latestReadyRevisionName,status.traffic[0].revisionName,status.traffic[0].percent)"
```

### API 验证

```bash
# 健康检查
curl -s -w "\nHTTP %{http_code}" https://life-agent-api-hyo2yvwwia-uc.a.run.app/health

# 未认证访问（应返回 401）
curl -s -w "\nHTTP %{http_code}" https://life-agent-api-hyo2yvwwia-uc.a.run.app/api/life/events

# 伪造 token（应返回 401）
curl -s -w "\nHTTP %{http_code}" -H "Authorization: Bearer fake-token" https://life-agent-api-hyo2yvwwia-uc.a.run.app/api/life/events

# Internal 无 token（应返回 401）
curl -s -w "\nHTTP %{http_code}" -X POST \
  -H "Content-Type: application/json" \
  -d '{"documentId":"t","userId":"t","gcsPath":"gs://t"}' \
  https://life-agent-api-hyo2yvwwia-uc.a.run.app/internal/api/v1/documents/process

# Internal 伪造 token（应返回 401）
curl -s -w "\nHTTP %{http_code}" -X POST \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer fake-token" \
  -d '{"documentId":"t","userId":"t","gcsPath":"gs://t"}' \
  https://life-agent-api-hyo2yvwwia-uc.a.run.app/internal/api/v1/documents/process
```

### 日志检查

```bash
gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=life-agent-api AND severity>=ERROR" \
  --project=copper-affinity-467409-k7 \
  --limit=5 \
  --format="value(timestamp,severity,textPayload)"
```

---

## 标准提示词模板

### 1. 开始一个 P0

```
请开始 Phase {N} P0 {编号}：{任务名称}。

背景：
- 当前阶段：Phase {N} {阶段名称}
- 上一个 P0 已完成并提交/推送
- 当前 API revision: {revision}
- 本地测试 {N} 个通过

要求：
1. 梳理当前相关代码和配置
2. 设计实现方案
3. 实现代码
4. 编写测试
5. 更新文档
6. 运行 dotnet test
7. 输出 diff

限制：
- 可以修改代码、测试、配置、文档
- 不要部署
- 不要提交，完成后先让我 review
- 不要执行 firebase deploy
- 不要改动无关功能
```

### 2. Review 当前 diff

```
请 review 当前所有未提交改动的 diff。

要求：
- 不要修改代码
- 不要提交
- 不要部署
- 只做审查和输出结论

请重点检查：
1. git status --short
2. git diff --stat
3. git diff
4. 按文件 review 每个改动
5. 检查是否有遗漏、错误、安全风险
6. 输出：是否可以提交、是否需要修正、发现的问题
```

### 3. 提交当前改动

```
请提交本次改动。

要求：
1. 提交前执行 git status --short
2. 确认只包含本次相关文件
3. 不要部署
4. 不要 push
5. 使用 commit message: {message}

提交后输出：
- commit hash
- git status --short
- 本次提交文件列表
```

### 4. 部署 API

```
请部署当前已提交的 API 到 Cloud Run。

要求：
1. 只部署 API 服务 life-agent-api
2. 不部署 Web
3. 部署前确认 git status --short 是干净的
4. 部署后确认：
   - Cloud Run 最新 revision
   - traffic 100% 指向最新 revision
   - 服务健康检查正常
5. 不要修改代码
6. 不要新增提交
7. 不要执行 firebase deploy
```

### 5. 部署后线上轻量验证

```
请对刚部署后的 API 做线上冒烟验证。

要求：
- 不要修改代码
- 不要部署
- 不要大量请求压测
- 只做轻量验证

请验证：
1. /health 正常返回
2. 未认证访问应返回 401
3. Internal 无 token / 伪造 token 返回 401
4. Cloud Run revision / traffic 检查
5. 日志无明显 500 / 配置异常

完成后输出验证结论和是否可以进入下一个 P0。
```

### 6. Phase 收尾判断

```
请判断当前 Phase {N} 是否可以收尾。

请检查：
1. 所有 P0 任务是否完成
2. 已完成任务是否已提交、推送、部署
3. 测试是否全部通过
4. 文档是否已更新
5. 是否有未解决的阻塞问题

输出：
- Phase 收尾判断
- 已完成 P0 清单
- 未完成项目（如有）
- 进入下一阶段的前置条件
- 建议下一步
```

---

## 冒烟验证清单

### 认证链路

| # | 验证项 | 目的 | 期望结果 | 需要认证 | LLM 成本 | Embedding 成本 | 可线上执行 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| A1 | /health 不需认证 | 确认健康检查不被 Auth 拦截 | 200 "healthy" | 否 | 无 | 无 | ✅ | 低 |
| A2 | 未认证访问返回 401 | 确认 Auth 中间件生效 | 401 Unauthorized | 否 | 无 | 无 | ✅ | 低 |
| A3 | 伪造 token 返回 401 | 确认 token 验签生效 | 401 | 否 | 无 | 无 | ✅ | 低 |
| A4 | Mock token 登录 | 确认 Development Mock 模式 | 200 + 用户数据 | 是 | 无 | 无 | ❌ 本地 | 低 |
| A5 | 真实 Firebase token 登录 | 确认 Production 认证 | 200 + 用户数据 | 是 | 无 | 无 | ✅ | 低 |

### RAG 文档处理链路

| # | 验证项 | 目的 | 期望结果 | 需要认证 | LLM 成本 | Embedding 成本 | 可线上执行 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| B1 | 上传小 PDF | 确认上传 + GCS 存储 | 202 Accepted | 是 | 无 | 无 | ✅ | 低 |
| B2 | 文档状态查询 | 确认元数据写入 | 返回文档列表 | 是 | 无 | 无 | ✅ | 低 |
| B3 | 文档处理回调 | 确认 Cloud Tasks 异步处理 | status → success | 是 | 无 | 中 | ⚠️ 谨慎，仅小样本 | 中 |
| B4 | 文档文本抽取 | 确认 PDF 文本提取 | 文本抽取成功 | 是 | 无 | 低 | ⚠️ 谨慎，仅小样本 | 低 |
| B5 | 文档分块 | 确认 chunk 生成 | chunks > 0 | 是 | 无 | 低 | ⚠️ 谨慎，仅小样本 | 低 |
| B6 | Embedding 生成 | 确认向量写入 | 768 维向量存在 | 是 | 无 | 中 | ⚠️ 谨慎，仅小样本（会产生 embedding 调用） | 中 |
| B7 | 文档删除 | 确认 GCS + 元数据清理 | 200 + 删除成功 | 是 | 无 | 无 | ✅ | 低 |

### RAG 问答链路

| # | 验证项 | 目的 | 期望结果 | 需要认证 | LLM 成本 | Embedding 成本 | 可线上执行 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| C1 | RAG 问答基本功能 | 确认端到端 RAG 流程 | 200 + 回答 + citations | 是 | 高 | 高 | ⚠️ 谨慎，仅小样本（会产生 LLM + embedding 调用） | 中 |
| C2 | 引用 citation 返回 | 确认引用有效性 | 有效引用 + 文件名 | 是 | 高 | 高 | ⚠️ 谨慎，仅小样本（会产生 LLM + embedding 调用） | 中 |
| C3 | 历史消息读取 | 确认会话持久化 | 返回历史消息 | 是 | 无 | 无 | ✅ | 低 |
| C4 | 清除历史消息 | 确认会话清理 | 200 + 消息删除 | 是 | 无 | 无 | ✅ | 低 |
| C5 | 无文档时 RAG 问答 | 确认无结果时的行为 | 200 + 无相关文档回答 | 是 | 高 | 高 | ⚠️ 谨慎，仅小样本（会产生 LLM + embedding 调用） | 中 |

### 成本保护链路

| # | 验证项 | 目的 | 期望结果 | 需要认证 | LLM 成本 | Embedding 成本 | 可线上执行 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| D1 | 超大文件拒绝 (>10MB) | 确认 FileValidator 生效 | 400/413 拒绝 | 是 | 无 | 无 | ✅ | 低 |
| D2 | Kestrel 请求体限制 (>15MB) | 确认 Kestrel 兜底 | 请求被拒绝 | 否 | 无 | 无 | ❌ 优先本地验证 | 低 |
| D3 | 每日 LLM 配额耗尽 | 确认配额保护 | 429 配额超限 | 是 | 高 | 无 | ❌ 本地 | 高 |
| D4 | 每日 Embedding 配额耗尽 | 确认配额保护 | 429 配额超限 | 是 | 无 | 高 | ❌ 本地 | 高 |
| D5 | Chunk 截断 (>200) | 确认 MaxChunksPerDocument | chunks ≤ 200, IsTruncated | 是 | 无 | 中 | ✅ | 中 |
| D6 | Rate Limit 超限 | 确认分钟级限流 | 429 请求过于频繁 | 是 | 高 | 高 | ❌ 本地 | 高 |

### Internal 端点安全

| # | 验证项 | 目的 | 期望结果 | 需要认证 | LLM 成本 | Embedding 成本 | 可线上执行 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| E1 | 无 token 拒绝 | 确认 OIDC 校验 | 401 Missing token | 否 | 无 | 无 | ✅ | 低 |
| E2 | 伪造 token 拒绝 | 确认 OIDC 验签 | 401 OIDC failed | 否 | 无 | 无 | ✅ | 低 |
| E3 | 跨租户请求拒绝 | 确认零信任校验 | 403 Access denied | 是 | 无 | 无 | ❌ 本地 | 低 |
| E4 | GCS 路径伪造拒绝 | 确认路径前缀校验 | 403 Path validation | 是 | 无 | 无 | ❌ 本地 | 低 |

### 基础设施

| # | 验证项 | 目的 | 期望结果 | 需要认证 | LLM 成本 | Embedding 成本 | 可线上执行 | 风险等级 |
|---|---|---|---|---|---|---|---|---|
| F1 | Cloud Run 健康 | 确认实例正常 | Ready | 否 | 无 | 无 | ✅ | 低 |
| F2 | Traffic 100% | 确认流量指向最新 | 100% latest revision | 否 | 无 | 无 | ✅ | 低 |
| F3 | Git 状态干净 | 确认无未提交改动 | 空 | 否 | 无 | 无 | ✅ | 低 |
| F4 | 单元测试通过 | 确认代码质量 | 全部通过 | 否 | 无 | 无 | ✅ | 低 |

> **D2 说明**：Kestrel 请求体限制（15MB）不建议在线上验证。线上请求可能被 Cloud Run 负载均衡、代理层提前拦截，返回的状态码不一定是稳定的 413。优先在本地 Development 环境验证。

---

## 输出格式规范

每次执行阶段检查后，必须按以下结构输出：

```
## 阶段检查结果

### 当前阶段
- Phase {N} P0 {编号}：{任务名称}

### 当前 Git 状态
- 分支：{branch}
- 最新 commit：{hash} {message}
- 工作区：干净 / 有未提交改动

### 当前改动摘要
| 文件 | 操作 | 说明 |
|---|---|---|

### 测试结果
- dotnet test：{N} 个通过，{M} 个失败

### 线上状态（如涉及部署）
- Revision：{revision}
- Traffic：{percent}%
- /health：{status}

### 风险点
- {风险描述}

### 判断
- 是否可以提交：✅ / ❌
- 是否可以 push：✅ / ❌
- 是否可以部署：✅ / ❌
- 是否可以进入下一阶段：✅ / ❌

### 建议下一步
- {具体建议}
```

---

## 部署清单

### 部署前检查

1. `git status --short` — 工作区干净
2. `git log --oneline -1` — 确认最新 commit 是目标版本
3. `dotnet test` — 全部通过
4. 确认没有未提交的本地改动

### 部署命令

> ⚠️ **以下命令仅供参考**。实际部署以项目现有 deploy 脚本或 `docs/cloud-run-deploy-skill.md` 中的部署方式为准。执行前必须确认 Dockerfile、build context、环境变量和服务配置。

```bash
gcloud run deploy life-agent-api \
  --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated
```

### 部署后验证

1. `gcloud run services describe` — 确认 revision 和 traffic
2. `curl /health` — 确认 200
3. 未认证访问 — 确认 401
4. 日志检查 — 无 500 / 配置异常

---

## 关键配置值

| 配置项 | Production | Development | 说明 |
|---|---|---|---|
| FileValidator MaxFileSizeMb | 10 | 10 | 文件上传大小限制 |
| Kestrel MaxRequestBodySizeMb | 15 | 15 | 请求体大小限制 |
| MaxChunksPerDocument | 200 | 200 | 文档 chunk 数量上限 |
| DailyLlmCallLimit | 200 | 200 | 每日 LLM 调用上限 |
| DailyEmbeddingCallLimit | 500 | 500 | 每日 Embedding 调用上限 |
| DailyDocumentProcessLimit | 20 | 20 | 每日文档处理上限 |
| RateLimit HighCost | 10/min | 1000/min | 高成本端点限流 |
| RateLimit AuthUser | 60/min | 1000/min | 已认证用户限流 |
| RateLimit Internal | 20/min | 1000/min | Internal 端点限流 |
| RateLimit GlobalIp | 30/min | 1000/min | 未认证 IP 限流 |

# Phase 3.5 / Phase 4 RAG E2E Smoke Test

> 目的：用低风险脚本覆盖线上或本地 RAG 主链路的基础验收，减少每次部署后完全依赖人工点击。

脚本位置：

```bash
scripts/smoke-rag-e2e.mjs
```

## 覆盖范围

默认无认证模式会执行：

- API `/health` 返回 `healthy`
- API 根路径有响应且不是 5xx；未提供认证时返回 401 也视为服务可达
- Web 根路径可访问

配置认证和显式写入开关后，会额外执行：

- 上传一个临时 Markdown 测试文档
- 轮询文档状态，等待 `processing` 变为 `success`
- 对该文档发起一次 RAG 问答
- 校验回答存在 `response` / `answer` / `content`
- 如果返回 citations，校验引用结构包含 `documentId` 和 `documentName`
- 删除临时测试文档
- 清理测试会话消息

## 运行方式

只读 smoke（未设置 `API_BASE_URL` 时只检查 Web，并明确跳过 API/RAG）：

```bash
node scripts/smoke-rag-e2e.mjs
```

只读 API + Web smoke：

```bash
API_BASE_URL="$(gcloud run services describe life-agent-api --region us-central1 --format='value(status.url)')" \
node scripts/smoke-rag-e2e.mjs
```

指定环境：

```bash
API_BASE_URL="https://life-agent-api-273399902864.asia-northeast1.run.app" \
WEB_BASE_URL="https://life.zhuchaofan.com" \
node scripts/smoke-rag-e2e.mjs
```

完整 RAG 闭环 smoke：

```bash
API_BASE_URL="https://life-agent-api-273399902864.asia-northeast1.run.app" \
WEB_BASE_URL="https://life.zhuchaofan.com" \
FIREBASE_ID_TOKEN="<firebase-id-token>" \
RUN_MUTATING_SMOKE=true \
node scripts/smoke-rag-e2e.mjs
```

## 环境变量

| 变量 | 必填 | 默认值 | 说明 |
|---|---:|---|---|
| `API_BASE_URL` | 否 | 空 | API 服务地址；为空时跳过 API/RAG，仅检查 Web。建议用 `gcloud run services describe life-agent-api --region us-central1 --format='value(status.url)'` 获取 |
| `WEB_BASE_URL` | 否 | `https://life.zhuchaofan.com` | Web 服务地址 |
| `FIREBASE_ID_TOKEN` | 否 | 空 | Firebase Auth ID token；为空时跳过认证步骤 |
| `RUN_MUTATING_SMOKE` | 否 | `false` | 必须为 `true` 才会创建和删除临时文档 |
| `SMOKE_RAG_QUESTION` | 否 | 测试问题 | RAG 问答内容 |
| `SMOKE_POLL_TIMEOUT_MS` | 否 | `120000` | 文档处理轮询超时 |
| `SMOKE_POLL_INTERVAL_MS` | 否 | `3000` | 文档处理轮询间隔 |

## 安全约束

- 不要把真实账号、token、cookie、API key 写进仓库。
- 生产环境默认只做只读 smoke。
- 完整 RAG 闭环必须显式设置 `RUN_MUTATING_SMOKE=true`，避免误写入真实用户数据。
- 建议使用专门的测试账号生成 `FIREBASE_ID_TOKEN`。
- 脚本创建的文档和会话使用临时 ID，并在 finally 阶段尝试清理。

## 当前限制

- Firebase 登录自动化未内置在脚本中；需要外部提供 `FIREBASE_ID_TOKEN`。
- 如果未配置 token，上传、RAG、删除、清会话步骤会显示为 `SKIP`。
- 如果文档处理队列或 LLM 服务异常，脚本会在对应步骤输出明确失败原因。

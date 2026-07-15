# Cloud Run 标准化部署流程 — LifeAgent

## 项目基本信息

| 项目 | 值 |
|---|---|
| GCP project | `copper-affinity-467409-k7` |
| region | `us-central1` |
| API service | `life-agent-api` |
| Web service | `life-agent-web` |
| Web custom domain | `https://life.zhuchaofan.com/` |
| Dockerfile（API） | `LifeAgent.Api/Dockerfile` |
| Dockerfile（Web） | `life-agent-web/Dockerfile` |
| 标准部署入口 | `scripts/deploy-lifeos.sh` |
| Web 部署脚本 | `life-agent-web/deploy.sh` |
| 环境变量文件 | `life-agent-web/.env.production` |

---

## 标准部署入口

优先使用项目根目录下的统一入口：

```bash
# 只判断将部署什么，不执行 Cloud Run 部署
scripts/deploy-lifeos.sh --dry-run

# 根据最近一次提交自动判断部署 API / Web / all / none
scripts/deploy-lifeos.sh

# 显式指定部署范围
scripts/deploy-lifeos.sh --target api
scripts/deploy-lifeos.sh --target web
scripts/deploy-lifeos.sh --target all
```

`--target auto` 的默认比较范围是 `HEAD~1..HEAD`，也可以显式传入：

```bash
scripts/deploy-lifeos.sh --target auto --since <REF>
```

如果连续多个本地 commit 尚未部署，`<REF>` 应使用**上一次已部署的 commit**，不要只依赖默认 `HEAD~1`。

自动部署范围判断：

| 变更范围 | 操作 |
|---|---|
| `LifeAgent.Api/**` 或解决方案入口 | 只部署 API |
| `life-agent-web/**` | 只部署 Web |
| API 与 Web 都有变更 | 先部署 API → 确认成功 → 再部署 Web |
| 只有 docs / scripts / tests / markdown | 不部署 |
| 无法安全归类的产品代码 | 部署 API + Web |

统一入口负责：

1. 检查工作区必须 clean
2. 执行 `git diff --check`
3. 执行后端测试、前端 lint、前端 build
4. 部署前确认 Web `.env.production` 的 `API_BASE_URL` 与当前 API Cloud Run URL 一致
5. 按 API → Web 顺序部署
6. 输出 revision、traffic、API health、自定义域名和最近 15 分钟 Cloud Run ERROR/500 日志

`--dry-run` 只输出将执行的动作，不执行 `gcloud run deploy`。

---

## 部署前检查

每次部署前 **必须全部通过** 才能继续：

```bash
# 1. 检查本地变更
git status
git diff --stat
git diff --check

# 2. 前端类型检查 + 构建
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web

# 3. 后端单元测试（从项目根目录运行）
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

**任一步失败则停止部署，修复后重新检查。**

> `npm run build --prefix life-agent-web` 使用 Next.js `next/font`。如果构建只因 `Failed to fetch Geist/Geist Mono` 失败，通常是构建环境无法访问 Google Fonts；联网重跑，或在单独变更中改为本地字体方案。

---

## 部署顺序

通常由 `scripts/deploy-lifeos.sh --target auto` 自动判断。手工 fallback 时遵守：

| 变更范围 | 操作 |
|---|---|
| **只有后端改动** | 只部署 API |
| **只有前端改动** | 只部署 Web |
| **前后端都有改动** | 先部署 API → 确认成功 → 再部署 Web |

---

## API 部署命令

以下命令只作为统一脚本不可用时的 fallback。

API Dockerfile (`LifeAgent.Api/Dockerfile`) 是自包含的，不依赖解决方案根目录或其他项目，所以使用 `--source ./LifeAgent.Api`：

```bash
# API 部署（从项目根目录运行）
gcloud run deploy life-agent-api \
  --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated
```

**为什么用 `--source ./LifeAgent.Api`**：Dockerfile 中 `COPY ["LifeAgent.Api.csproj", "./"]` 要求构建上下文是 `LifeAgent.Api/` 目录本身。从项目根目录跑 `--source .` 会导致 COPY 找不到 `.csproj` 的相对路径。

---

## 获取当前 API URL

部署 Web 前必须确认 API URL 正确：

```bash
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="value(status.url)"
```

输出类似：`https://life-agent-api-hyo2yvwwia-uc.a.run.app`

---

## Web 部署命令

以下命令只作为统一脚本不可用时的 fallback。

Web 使用项目自带的部署脚本（`life-agent-web/deploy.sh`）：

```bash
# Web 部署（从 life-agent-web 目录运行）
cd life-agent-web && ./deploy.sh
```

**部署前必须确认**：
1. `.env.production` 中的 `API_BASE_URL` 指向 **当前 API Cloud Run URL**（用上面的命令获取）
2. 不要误用 `localhost`、旧 API URL、或错误服务的 URL
3. 变量名是 `API_BASE_URL`（不是 `NEXT_PUBLIC_API_BASE_URL`）

> `.env.production` 被 `deploy.sh` 读取，通过 `--set-env-vars` 和 `--set-build-env-vars` 传入 Cloud Run。

---

## 禁止事项

以下操作 **绝对禁止**：

- 未经用户明确要求执行真实部署
- 修改域名映射
- 重新创建 Cloud Run 服务（`gcloud run deploy` 只应更新已有服务）
- 修改 Firebase 项目配置
- 修改 Cloud Run env 或开启新的写入开关
- 在生产环境启用 mock auth（`USE_MOCK_AUTH=true`）
- **盲目使用 `gcloud run deploy --source .`（项目根目录）**——必须确认构建上下文目录正确
- 构建失败后 **重复重试同一个部署命令** ——必须先查日志
- **在未确认 Dockerfile / 构建上下文 / 脚本含义前直接部署**

---

## 构建失败排查

部署失败后，先查构建日志，不要重复执行同一条命令：

```bash
# 列出最近的构建
gcloud builds list \
  --project=copper-affinity-467409-k7 \
  --limit=5

# 查看具体构建日志
gcloud builds log <BUILD_ID> \
  --project=copper-affinity-467409-k7
```

常见原因：

| 错误 | 原因 | 修复 |
|---|---|---|
| COPY 失败 / file not found | 构建上下文目录不对 | 确认 `--source` 路径与 Dockerfile 的 COPY 一致 |
| 构建超时 | 依赖下载或编译耗时过长 | 检查网络/Dockerfile 是否有死循环 |
| 部署后服务不可用 | 环境变量缺失或端口不对 | 检查 `--set-env-vars` 和 Dockerfile EXPOSE |

---

## 部署后验证清单

部署完成后 **逐项验证**：

1. 打开 `https://life.zhuchaofan.com/`
2. 登录是否正常
3. RAG 对话是否正常（提问并收到带引用的回复）
4. RAG 清除对话是否正常（点击垃圾桶图标 → 确认 → 消息清空）
5. 文档上传是否正常
6. 文档删除是否正常
7. 文档选择状态在清除对话后是否保留
8. 移动端/窄屏布局是否正常（无横向滚动条）
9. 页面整体无横向滚动条

验证命令：

```bash
# API 健康检查
curl https://life-agent-api-hyo2yvwwia-uc.a.run.app/health

# 确认部署的 revision 和 traffic
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="value(status.latestReadyRevisionName, status.traffic)"

gcloud run services describe life-agent-web \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="value(status.latestReadyRevisionName, status.traffic)"
```

### 生产安全校验

API 部署完成后必须确认生产环境未启用 mock auth：

```bash
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="json(spec.template.spec.containers[0].env)"
```

检查输出中的 `USE_MOCK_AUTH`：

- 允许：`USE_MOCK_AUTH=false`
- 允许：未设置 `USE_MOCK_AUTH`
- 禁止：`USE_MOCK_AUTH=true`

生产环境必须使用真实 Firebase ID Token 验签。`USE_MOCK_AUTH=true` 只允许本地开发调试使用，绝不能出现在 Cloud Run 生产服务配置中。

### Firestore Rules 当前状态

- 本地 `firestore.rules` 已存在，`firebase.json` 指向该 rules 文件。
- `.firebaserc` 当前指向 Firestore 数据项目 `copper-affinity-467409-k7`。
- 当前线上 Firestore ruleset/release 状态未确认或未部署完成，不应标记为"已完成"。
- 当前架构中 Firestore 数据项目为 `copper-affinity-467409-k7`，Firebase Auth 项目为 `my-agent-app-a5e42`。
- 跨项目 Auth 问题解决前，不要直接把 Firestore Rules 部署状态标记为完成；否则 `request.auth` 可能无法匹配同项目 Auth，导致 rules 验证结果与预期不一致。
- 现阶段业务数据访问仍应通过后端 BFF/API 完成，不要新增前端直连 Firestore 的生产读写路径。

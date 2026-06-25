 # Cloud Run 部署指南 — LifeAgent
 
 ## 架构概览
 
 ```
 用户浏览器 ──► Next.js Cloud Run ──► .NET API Cloud Run ──► Firestore
                      │                       │
                      │                       ├── Firebase Auth (验签)
                      │                       └── Gemini API
                      │
                      └── Firebase Auth SDK (登录)
 ```
 
 **关键设计**：所有 API 调用都走 Next.js **Server Actions**（服务端发起的 `fetch`），
 不存在浏览器直接请求 .NET API 的情况，因此 **不需要配置 CORS**。
 
 认证流程：
 
 1. 浏览器端通过 Firebase SDK `signInWithPopup` 获取 idToken
 2. Server Action `login()` 将 idToken 存入 httpOnly cookie
 3. 后续 Server Action 从 cookie 读出 idToken，放入 `Authorization: Bearer` 请求头
 4. .NET API 的 `FirebaseAuthMiddleware` 用 Firebase Admin SDK 验签
 
 ## 目录
 
 - [后端 Cloud Run 环境变量](#后端-cloud-run-环境变量)
 - [前端 Cloud Run 环境变量](#前端-cloud-run-环境变量)
 - [IAM 权限](#iam-权限)
 - [构建 & 部署命令](#构建--部署命令)
 - [部署后验证](#部署后验证)
 - [本地开发 vs Cloud Run 差异对照](#本地开发-vs-cloud-run-差异对照)
 - [排错清单](#排错清单)
 
 ## 后端 Cloud Run 环境变量
 
 ### 必需
 
 | 变量名 | 说明 | 来源 |
 |---|---|---|
 | `FIRESTORE_PROJECT_ID` | Firestore 所在的 GCP 项目 ID（默认为 `copper-affinity-467409-k7`，建议显式指定） | GCP Console |
 
 ### 条件必需
 
 | 变量名 | 何时需要 | 说明 | 来源 |
 |---|---|---|---|
 | `GEMINI_API_KEY` | `USE_MOCK_LLM` 未设置或不为 `true` | Gemini API Key，代码中优先读 `Gemini:ApiKey` 配置，其次读 `GEMINI_API_KEY` 环境变量 | [Google AI Studio](https://aistudio.google.com/apikey) |
 
 ### 选项
 
 | 变量名 | 建议 | 说明 |
 |---|---|---|
 | `USE_MOCK_LLM` | **Cloud Run 上不要设** | 设为 `true` 则使用 Mock 解析器绕过 Gemini API（仅本地开发用） |
 | `Gemini__Model` | 可不设 | 覆盖 Gemini 模型名，默认 `gemini-2.5-flash` |
 
 ### ⚠️ 禁止在生产环境设置
 
 | 变量名 | 原因 |
 |---|---|
 | `USE_MOCK_AUTH` | 设为 `true` 会跳过所有鉴权，所有人都是 `test_user_01` |
 
 ### 不需要设置
 
 | 变量名 | 说明 |
 |---|---|
 | `ASPNETCORE_URLS` | Dockerfile 中已设为 `http://+:8080` |
 | `GOOGLE_APPLICATION_CREDENTIALS` | Cloud Run 自动注入 ADC，无需手动指定 |
 
 ## 前端 Cloud Run 环境变量
 
 ### 必需
 
 | 变量名 | 说明 | 来源 |
 |---|---|---|
 | `API_BASE_URL` | 后端 Cloud Run 的服务 URL，例如 `https://lifeagent-api-xxxxx-uc.a.run.app` | 后端部署完成后获得 |
 | `NEXT_PUBLIC_FIREBASE_API_KEY` | Firebase Web App 的 API Key | Firebase Console → Project Settings → General → Your apps |
 | `NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN` | Firebase Auth Domain，格式 `<project-id>.firebaseapp.com` | Firebase Console |
 | `NEXT_PUBLIC_FIREBASE_PROJECT_ID` | Firebase 项目 ID | Firebase Console |
 
 ### 已在 Dockerfile 中设置，无需重复
 
 | 变量 | 值 |
 |---|---|
 | `NODE_ENV` | `production` |
 | `PORT` | `8080` |
 | `HOSTNAME` | `0.0.0.0` |
 | `NEXT_TELEMETRY_DISABLED` | `1` |
 
 ## IAM 权限
 
 Cloud Run 使用一个 **运行时服务账号**（Runtime Service Account）来运行容器。
 默认是 Compute Engine default service account（`PROJECT_NUMBER-compute@developer.gserviceaccount.com`），
 也推荐单独创建一个自定义服务账号。
 
 ### 后端 Cloud Run 服务账号需要的权限
 
 | 角色 | 用途 |
 |---|---|
 | `roles/firebase.authenticationAdmin` | Firebase Auth ID Token 验签（`VerifyIdTokenAsync`） |
 | `roles/datastore.user` | Firestore 读写（创建/查询 LifeEvent） |
 
 也可以使用 `roles/firebase.admin`（包含 AuthenticationAdmin 以及其他 Firebase 权限）。
 
 ### 授权命令
 
 ```bash
 PROJECT_ID="copper-affinity-467409-k7"
 SA_EMAIL="lifeagent-api-sa@${PROJECT_ID}.iam.gserviceaccount.com"
 
 # 创建专用服务账号（可选，推荐）
 gcloud iam service-accounts create lifeagent-api-sa \
   --display-name "LifeAgent API Service Account"
 
 # 授予 Firebase Authentication Admin
 gcloud projects add-iam-policy-binding "$PROJECT_ID" \
   --member="serviceAccount:${SA_EMAIL}" \
   --role="roles/firebase.authenticationAdmin"
 
 # 授予 Firestore User
 gcloud projects add-iam-policy-binding "$PROJECT_ID" \
   --member="serviceAccount:${SA_EMAIL}" \
   --role="roles/datastore.user"
 ```
 
 ### 前端 Cloud Run 服务账号
 
 前端（Next.js）不需要特殊 IAM 权限——它只通过 HTTP 访问后端 API，不直接访问 GCP 资源。
 使用默认服务账号即可。
 
 ## 构建 & 部署命令
 
 ### 1. 构建并推送后端
 
 ```bash
 SERVICE="lifeagent-api"
 REGION="asia-east1"
 PROJECT_ID="copper-affinity-467409-k7"
 SA_EMAIL="lifeagent-api-sa@${PROJECT_ID}.iam.gserviceaccount.com"
 
 gcloud builds submit ./LifeAgent.Api \
   --tag "${REGION}-docker.pkg.dev/${PROJECT_ID}/cloud-run-source-deploy/${SERVICE}:latest"
 
 gcloud run deploy "${SERVICE}" \
   --image "${REGION}-docker.pkg.dev/${PROJECT_ID}/cloud-run-source-deploy/${SERVICE}:latest" \
   --region "${REGION}" \
   --platform managed \
   --allow-unauthenticated \
   --memory 512Mi \
   --concurrency 80 \
   --min-instances 0 \
   --max-instances 2 \
   --service-account "${SA_EMAIL}" \
   --set-env-vars "^--^FIRESTORE_PROJECT_ID=${PROJECT_ID},GEMINI_API_KEY=your-gemini-key-here"
 
 # 记录输出的 URL，部署前端时需要
 ```
 
 ### 2. 构建并推送前端
 
 ```bash
 SERVICE="lifeagent-web"
 API_URL="https://lifeagent-api-xxxxx-uc.a.run.app"  # 替换为上一步输出的 URL
 REGION="asia-east1"
 PROJECT_ID="copper-affinity-467409-k7"
 
 gcloud builds submit ./life-agent-web \
   --tag "${REGION}-docker.pkg.dev/${PROJECT_ID}/cloud-run-source-deploy/${SERVICE}:latest"
 
 gcloud run deploy "${SERVICE}" \
   --image "${REGION}-docker.pkg.dev/${PROJECT_ID}/cloud-run-source-deploy/${SERVICE}:latest" \
   --region "${REGION}" \
   --platform managed \
   --allow-unauthenticated \
   --memory 256Mi \
   --concurrency 80 \
   --min-instances 0 \
   --max-instances 2 \
   --set-env-vars "^--^API_BASE_URL=${API_URL},NEXT_PUBLIC_FIREBASE_API_KEY=your-api-key,NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN=your-domain,NEXT_PUBLIC_FIREBASE_PROJECT_ID=${PROJECT_ID}"
 ```
 
 > **注意**：`--set-env-vars` 中的 `^--^` 是分隔符转义，避免 `,` 被解析为多个 flag。
 > 如果变量较多，建议使用 `--env-vars-file` 传入 YAML 文件。
 
 ### 使用 env-vars-file（推荐）
 
 创建 `backend.env.yaml`:
 
 ```yaml
 FIRESTORE_PROJECT_ID: copper-affinity-467409-k7
 GEMINI_API_KEY: your-gemini-key-here
 ```
 
 ```bash
 gcloud run deploy lifeagent-api \
   --env-vars-file backend.env.yaml \
   ...
 ```
 
 ## 部署后验证
 
 ### 后端健康检查
 
 ```bash
 curl https://lifeagent-api-xxxxx-uc.a.run.app/health
 # 期望返回: healthy
 ```
 
 ### 完整端到端验证
 
 1. 打开前端 URL（`lifeagent-web` 的 `.run.app` 地址）
 2. 点击 "Sign in with Google"，完成 Firebase 登录
 3. 在文本框中输入 "今天骑行 18km"，点击 Submit
 4. 确认 Timeline 中出现对应事件
 
 ### 验证后端日志
 
 ```bash
 gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=lifeagent-api AND severity>=WARNING" --limit 20
 ```
 
 ## 本地开发 vs Cloud Run 差异对照
 
 | 项目 | 本地开发 | Cloud Run |
 |---|---|---|
 | 后端端口 | `localhost:5140` | `:8080`（Dockerfile 设定） |
 | 前端 `API_BASE_URL` | `http://localhost:5140` | 后端 `.run.app` URL |
 | Firebase Auth | `USE_MOCK_AUTH=true` | 不设（真实验签） |
 | Gemini API | `USE_MOCK_LLM=true` 或真实 Key | 真实 Key |
 | ADC 凭证 | `gcloud auth application-default login` | Cloud Run 运行时自动注入 |
 | 服务端与客户端域名 | 同域 `localhost:3000` 和 `localhost:5140` | 不同域，但请求走服务端，无需 CORS |
 
 ## 排错清单
 
 ### 登录失败 / "Sign in with Google" 无响应
 
 - 检查 `NEXT_PUBLIC_FIREBASE_*` 三个变量是否已正确设置到前端 Cloud Run
 - 检查 Firebase Console → Authentication → Sign-in method 中 Google 是否启用
 - 检查 Firebase Console → 授权域列表中是否包含前端 `.run.app` 域名
 
 ### 提交事件后 401/403
 
 - 检查后端 **没有** 设 `USE_MOCK_AUTH=true`
 - 检查后端 Cloud Run 服务账号是否有 `firebase.authenticationAdmin` 角色
 - 检查后端日志：`gcloud logging read "resource.labels.service_name=lifeagent-api" --limit 20`
 
 ### 提交事件后 500 / "Gemini API 调用失败"
 
 - 检查 `GEMINI_API_KEY` 是否正确设置到后端
 - 临时设 `USE_MOCK_LLM=true` 排除 Gemini 问题
 
 ### 后端可访问但前端报 Network Error
 
 - 确认前端 `API_BASE_URL` 是后端 `.run.app` URL（而非 `localhost`）
 - 确认后端 Cloud Run 为 `--allow-unauthenticated`（允许公开访问）
 - 前端 Server Action 在 Next.js 服务器端发起 `fetch`，不受浏览器同源策略限制，但仍需保证网络可达
 
 ### 部署后页面空白 / 500
 
 ```bash
 gcloud logging read "resource.type=cloud_run_revision AND resource.labels.service_name=lifeagent-web AND severity>=ERROR" --limit 20
 ```

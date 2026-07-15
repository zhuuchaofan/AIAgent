#!/bin/bash

# 遇到错误即退出
set -euo pipefail

echo "🚀 开始部署 life-agent-web 到 Cloud Run..."

# 1. 从 .env.production 加载环境变量
if [ -f .env.production ]; then
  echo "📂 正在加载 .env.production 环境变量..."
  # 忽略注释和空行加载环境变量
  export $(grep -v '^#' .env.production | xargs)
else
  echo "❌ 错误: 找不到 .env.production 文件！"
  exit 1
fi

# 2. 验证关键环境变量是否存在
if [ -z "$NEXT_PUBLIC_FIREBASE_PROJECT_ID" ]; then
  echo "❌ 错误: 未能在 .env.production 中提取到 NEXT_PUBLIC_FIREBASE_PROJECT_ID"
  exit 1
fi

NEXT_PUBLIC_ENABLE_AGENT_PREVIEW="${NEXT_PUBLIC_ENABLE_AGENT_PREVIEW:-false}"

# 3. 执行 gcloud 部署指令
echo "☁️  正在推送到 Google Cloud Run (us-central1)..."
echo "这可能需要几分钟时间来构建 Docker 镜像..."

gcloud run deploy life-agent-web \
  --source . \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated \
  --set-env-vars NEXT_PUBLIC_FIREBASE_API_KEY="${NEXT_PUBLIC_FIREBASE_API_KEY}",NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN="${NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN}",NEXT_PUBLIC_FIREBASE_PROJECT_ID="${NEXT_PUBLIC_FIREBASE_PROJECT_ID}",NEXT_PUBLIC_FIREBASE_APP_ID="${NEXT_PUBLIC_FIREBASE_APP_ID}",API_BASE_URL="${API_BASE_URL}",NEXT_PUBLIC_ENABLE_AGENT_PREVIEW="${NEXT_PUBLIC_ENABLE_AGENT_PREVIEW}" \
  --set-build-env-vars NEXT_PUBLIC_FIREBASE_API_KEY="${NEXT_PUBLIC_FIREBASE_API_KEY}",NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN="${NEXT_PUBLIC_FIREBASE_AUTH_DOMAIN}",NEXT_PUBLIC_FIREBASE_PROJECT_ID="${NEXT_PUBLIC_FIREBASE_PROJECT_ID}",NEXT_PUBLIC_FIREBASE_APP_ID="${NEXT_PUBLIC_FIREBASE_APP_ID}",NEXT_PUBLIC_ENABLE_AGENT_PREVIEW="${NEXT_PUBLIC_ENABLE_AGENT_PREVIEW}"

echo "✅ 部署流程结束！"

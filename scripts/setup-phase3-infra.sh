#!/usr/bin/env bash

# ==============================================================================
# Phase 3 RAG 基础设施自动化配置脚本 (Setup GCP Infrastructure)
# 
# 使用说明:
# 1. 确保已在本地安装 Google Cloud SDK (gcloud CLI)。
# 2. 运行 'gcloud auth login' 并登录有足够 IAM 管理权限的账号。
# 3. 运行此脚本: ./scripts/setup-phase3-infra.sh [PROJECT_ID] [ENV]
#    - PROJECT_ID: GCP 项目 ID (默认: copper-affinity-467409-k7)
#    - ENV: 环境名称 (dev 或 prod，默认: dev)
# ==============================================================================

# 强阻断：任何命令失败立即退出
set -euo pipefail

# 默认参数
DEFAULT_PROJECT="copper-affinity-467409-k7"
DEFAULT_ENV="dev"

PROJECT_ID="${1:-$DEFAULT_PROJECT}"
ENV="${2:-$DEFAULT_ENV}"

# 配置命名
BUCKET_NAME="lifeagent-rag-documents-${PROJECT_ID}"
QUEUE_NAME="rag-document-processing-queue"
LOCATION="us-central1"

if [ "$ENV" = "dev" ]; then
    BUCKET_NAME="${BUCKET_NAME}-dev"
    QUEUE_NAME="${QUEUE_NAME}-dev"
fi

echo "======================================================================"
echo "🚀 开始配置 Phase 3 基础设施"
echo "   GCP 项目 ID : ${PROJECT_ID}"
echo "   部署环境    : ${ENV}"
echo "   地理位置    : ${LOCATION}"
echo "   GCS 存储桶  : gs://${BUCKET_NAME}"
echo "   Cloud Tasks : ${QUEUE_NAME}"
echo "======================================================================"

# 1. 设置当前的 gcloud 项目上下文
echo "Step 1: 切换 gcloud 核心项目配置..."
gcloud config set project "${PROJECT_ID}"

# 2. 启用必要的 API 服务
echo "Step 2: 确认启用必要的 GCP APIs (Storage, Cloud Tasks, Datastore, AI Platform)..."
gcloud services enable \
    storage.googleapis.com \
    cloudtasks.googleapis.com \
    firestore.googleapis.com \
    aiplatform.googleapis.com \
    --project="${PROJECT_ID}"

# 3. 创建 Cloud Storage 存储桶
echo "Step 3: 创建 GCS 存储桶 (gs://${BUCKET_NAME})..."
if gcloud storage buckets describe "gs://${BUCKET_NAME}" &>/dev/null; then
    echo "ℹ️ GCS 存储桶 gs://${BUCKET_NAME} 已存在，跳过创建。"
else
    gcloud storage buckets create "gs://${BUCKET_NAME}" \
        --project="${PROJECT_ID}" \
        --location="${LOCATION}" \
        --uniform-bucket-level-access
    echo "✅ GCS 存储桶 gs://${BUCKET_NAME} 创建成功。"
fi

# 4. 创建 Cloud Tasks 任务队列
echo "Step 4: 创建 Cloud Tasks 队列 (${QUEUE_NAME})..."
if gcloud tasks queues describe "${QUEUE_NAME}" --location="${LOCATION}" &>/dev/null; then
    echo "ℹ️ Cloud Tasks 队列 ${QUEUE_NAME} 已存在，跳过创建。"
else
    MAX_CONCURRENT=5
    MAX_ATTEMPTS=3
    MIN_BACKOFF="5s"
    MAX_BACKOFF="60s"
    
    if [ "$ENV" = "dev" ]; then
        MAX_CONCURRENT=2
        MAX_ATTEMPTS=2
        MIN_BACKOFF="2s"
        MAX_BACKOFF="30s"
    fi

    gcloud tasks queues create "${QUEUE_NAME}" \
        --project="${PROJECT_ID}" \
        --location="${LOCATION}" \
        --max-concurrent-dispatches="${MAX_CONCURRENT}" \
        --max-attempts="${MAX_ATTEMPTS}" \
        --min-backoff="${MIN_BACKOFF}" \
        --max-backoff="${MAX_BACKOFF}"
    echo "✅ Cloud Tasks 队列 ${QUEUE_NAME} 创建成功。"
fi

# 5. 创建 Firestore 向量复合索引
echo "Step 5: 配置 Firestore Vector Search 复合索引..."
echo "ℹ️ 向量索引需要使用 gcloud alpha 级指令创建。检查是否存在..."

# 注意：Firestore 复合索引对于 COLLECTION 作用域，同一个 CollectionGroup 结构在多租户下是通用的。
# 这里由于创建索引属于幂等操作，可以直接发起。
if gcloud alpha firestore indexes composite list --project="${PROJECT_ID}" --collection-group=chunks 2>/dev/null | grep -q "embedding"; then
    echo "ℹ️ Firestore 集合组 chunks 上的 embedding 向量索引已存在，跳过创建。"
else
    echo "⚠️ 正在后台向 Firestore 提交向量索引创建请求..."
    echo "   (集合: chunks, 字段: embedding, 维度: 768, 算法: COSINE)"
    gcloud alpha firestore indexes composite create \
        --project="${PROJECT_ID}" \
        --collection-group=chunks \
        --query-scope=COLLECTION \
        --field-config=field-path=embedding,vector-config='{"dimension":"768","flat":{}}'
    echo "✅ Firestore 复合向量索引配置命令已投递。注意：后台完成索引编译通常需要 3-10 分钟。"
fi

echo "======================================================================"
echo "🎉 Phase 3 基础设施配置完成！"
echo "   请参考 docs/phase3/infrastructure_setup.md 配置相关 Service Account 的 IAM 最小权限。"
echo "======================================================================"

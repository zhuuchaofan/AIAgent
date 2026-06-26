# Phase 3 基础设施配置说明指南 (Infrastructure Setup Guide)

本指南详细说明了 Phase 3 RAG 业务所需的 GCP 基础设施资源及其配置方式，包括 Google Cloud Storage (GCS)、Cloud Tasks 以及 Firestore 向量索引的详细规范与 IAM 权限要求。

---

## 1. 基础设施拓扑与资源命名规范

为了保证环境隔离，所有资源命名遵循格式：`{projectName}-{resourceName}-{projectId}[-env]`。

| 资源类型 | 逻辑角色 | 默认名称 (Production) | 默认名称 (Development) | 地理位置 | 备注 |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **GCS Bucket** | 存储用户上传的源物理文件 | `lifeagent-rag-documents-copper-affinity-467409-k7` | `lifeagent-rag-documents-copper-affinity-467409-k7-dev` | `us-central1` | 标准存储类 (Standard)，开启版本控制 (Optional) |
| **Cloud Tasks Queue** | 异步解析与分块计算向量的任务队列 | `rag-document-processing-queue` | `rag-document-processing-queue-dev` | `us-central1` | 限制并发以保护后台解析服务与 LLM 配额 |
| **Firestore Vector Index** | 提供 `users/{userId}/chunks` 的 768d 余弦相似度最近邻召回 | 复合索引：集合 `chunks`，字段 `embedding` | 复合索引：集合 `chunks`，字段 `embedding` | 继承 Firestore 默认数据库位置 | `768` 维，度量算法为 `COSINE` |

---

## 2. GCP 资源创建步骤与 gcloud 命令

### 2.1 创建 Cloud Storage Bucket

RAG 系统需要一个专用的存储桶来保存用户上传的源文件（例如 Markdown, PDF, TXT）。
使用以下命令创建（注意使用 Uniform Bucket-level Access 强化安全）：

```bash
# 创建生产环境存储桶
gcloud storage buckets create gs://lifeagent-rag-documents-copper-affinity-467409-k7 \
    --project=copper-affinity-467409-k7 \
    --location=us-central1 \
    --uniform-bucket-level-access

# 创建开发/测试环境存储桶
gcloud storage buckets create gs://lifeagent-rag-documents-copper-affinity-467409-k7-dev \
    --project=copper-affinity-467409-k7 \
    --location=us-central1 \
    --uniform-bucket-level-access
```

### 2.2 创建 Cloud Tasks Queue

由于大文件解析、OCR（如 PDF 提取）以及 Embedding API 调用非常消耗资源且容易触碰 LLM Rate Limit，因此必须使用 Cloud Tasks 做异步削峰填谷。
创建队列并限制最大并发数为 `5`（可配置）：

```bash
# 创建生产队列
gcloud tasks queues create rag-document-processing-queue \
    --project=copper-affinity-467409-k7 \
    --location=us-central1 \
    --max-concurrent-dispatches=5 \
    --max-attempts=3 \
    --min-backoff=5s \
    --max-backoff=60s

# 创建开发队列
gcloud tasks queues create rag-document-processing-queue-dev \
    --project=copper-affinity-467409-k7 \
    --location=us-central1 \
    --max-concurrent-dispatches=2 \
    --max-attempts=2 \
    --min-backoff=2s \
    --max-backoff=30s
```

### 2.3 创建 Firestore 向量复合索引

我们必须在 `users/{userId}/chunks/{chunkId}` 的 `chunks` 集合上，对包含 `__vector__` 类型的 `embedding` 字段建立 768 维的余弦度量索引。
使用 `gcloud alpha` 命令来创建复合向量索引：

```bash
# 创建复合向量索引（生产与开发共享同一个 Firestore 数据库实例的不同租户路径）
gcloud alpha firestore indexes composite create \
    --project=copper-affinity-467409-k7 \
    --collection-group=chunks \
    --query-scope=COLLECTION \
    --field-config=field-path=embedding,vector-config='{"dimension":"768","flat":{}}'
```
> [!NOTE]
> 1. 上述命令创建的是 `FLAT` 类型的向量索引（精确保留度最高）。如果数据量极大，未来可考虑采用 `IVF`（倒排文件算法）进行性能调优。
> 2. Firestore 创建索引需要一定时间，在此期间状态为 `CREATING`，待其变为 `READY` 后最近邻检索 API 才能正常响应。

---

## 3. IAM 权限矩阵与安全防护

后端 API 服务（以 App Engine、Cloud Run 的 Service Account 或是本地调试的 Application Default Credentials 身份运行）需要下列权限来调用 GCP 资源。

### 3.1 最小权限原则配置 (Least Privilege)

| 服务账号 (Service Account) | 作用目标资源 | IAM 角色 (IAM Role) | 授权目的 |
| :--- | :--- | :--- | :--- |
| `lifeagent-backend-sa` | `gs://lifeagent-rag-documents-*` | **Storage Object Admin** (`roles/storage.objectAdmin`) | 上传用户源文件，读取物理内容供解析器分块。 |
| `lifeagent-backend-sa` | `rag-document-processing-queue` | **Cloud Tasks Enqueuer** (`roles/cloudtasks.enqueuer`) | 向队列中推送解析任务。 |
| `lifeagent-backend-sa` | 后台解析触发端点 (如 Cloud Run) | **Cloud Run Invoker** (`roles/run.invoker`) | 授权 Cloud Tasks 使用 OIDC 令牌安全回调 API 的私有解析端点。 |
| `lifeagent-backend-sa` | Firestore (Datastore) | **Datastore User** (`roles/datastore.user`) | 进行普通的 chunks 读写，以及对向量数据的 REST 查询。 |
| `lifeagent-backend-sa` | Gemini (Vertex AI API) | **Vertex AI User** (`roles/aiplatform.user`) | 授权调用 `gemini-embedding-001` 模型接口生成向量。 |

### 3.2 任务触发授权 (Cloud Tasks OIDC Callback Authentication)

为了保障系统后台端点的安全性，用于处理分块解析的后台 HTTP Trigger 端点**不应对外公开公开访问**。我们应使用 Cloud Tasks 的 OIDC 服务账号关联机制。
- 当向 Cloud Tasks 投递任务时，配置 `oidcToken` 属性，将其 `serviceAccountEmail` 指向 `lifeagent-backend-sa`，并设置 `audience` 为后端触发的实际 URL（如 `https://<api-service-url>/api/v1/rag/internal/process-document`）。
- 后端在接收到请求后，必须通过中间件对请求头中的 `Authorization: Bearer <ID_Token>` 进行验签，确认其由 Google 签发且 Audience 严格匹配，防止外部恶意越权触发昂贵的解析操作。

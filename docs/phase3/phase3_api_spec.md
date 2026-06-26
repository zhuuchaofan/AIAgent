# Phase 3 RAG API 接口规范 (API Spec)

## 一、 鉴权与全局约束

1. **用户端 Authorization 校验**：所有面向用户的 API 请求必须在 Header 中携带 Bearer Token 进行鉴权。
   ```http
   Authorization: Bearer <Firebase_ID_Token>
   ```
   后端拦截并解析该 Token 提取出当前用户的 `userId`。

2. **全局错误返回格式**：统一格式如下：
   ```json
   {
     "error": {
       "code": "BAD_REQUEST",
       "message": "错误详细描述信息",
       "requestId": "5a41bf37-8e6d-4952-bda4-48ee732e7cb3"
     }
   }
   ```

---

## 二、 文档管理 API (Document Management)

### 1. 上传个人文档 (Upload Document)
用户上传单个文件。BFF 接收到后流式写入 Cloud Storage，在 Firestore 初始化元数据，并向 Cloud Tasks 投递异步 Ingestion 任务。

- **HTTP Method**: `POST`
- **Path**: `/api/v1/documents`
- **Content-Type**: `multipart/form-data`
- **Request Body**:
  - `file`: File (Multipart binary, 最大 10MB。支持 `.pdf`, `.txt`, `.md`)。
- **Response**:
  - **Status Code**: `202 Accepted`
  - **Body**:
    ```json
    {
      "documentId": "doc_8f7b2c9d1e4a3",
      "fileName": "2026年骑行训练计划.pdf",
      "fileSize": 1048576,
      "mimeType": "application/pdf",
      "status": "processing", // uploading -> processing (已提交 Cloud Tasks 异步任务)
      "createdAt": "2026-06-26T05:36:00Z"
    }
    ```

### 2. 获取文档列表 (List Documents)
获取已上传文档列表。

- **HTTP Method**: `GET`
- **Path**: `/api/v1/documents`
- **Query Parameters**:
  - `pageSize`: Integer (默认 10, 最大 50)
  - `cursor`: String (上一页最后一条记录的 Document ID，游标分页)
- **Response**:
  - **Status Code**: `200 OK`
  - **Body**:
    ```json
    {
      "items": [
        {
          "documentId": "doc_8f7b2c9d1e4a3",
          "fileName": "2026年骑行训练计划.pdf",
          "fileSize": 1048576,
          "mimeType": "application/pdf",
          "status": "success", // uploading, processing, deleting, success, failed
          "chunkCount": 18,
          "createdAt": "2026-06-26T05:36:00Z",
          "updatedAt": "2026-06-26T05:36:45Z",
          "errorMessage": null
        }
      ],
      "nextCursor": "doc_8f7b2c9d1e4a3"
    }
    ```

### 3. 删除文档 (Delete Document)
级联清理 Cloud Storage 物理文件和关联的 Chunks 向量。由于 Chunks 数量多时易超时，接口支持异步标记与分批清理。

- **HTTP Method**: `DELETE`
- **Path**: `/api/v1/documents/{documentId}`
- **Response**:
  - **Status Code**: `200 OK`
  - **Body**:
    ```json
    {
      "success": true,
      "documentId": "doc_8f7b2c9d1e4a3",
      "status": "deleting", // deleting（异步进行中） 或 success（同步已完成）
      "deletedChunksCount": 45, // 本次请求执行实际物理删除的 chunk 数量（基于执行反馈，不进行假设）
      "message": "文档已被标记为删除状态，后台分批清理任务已启动。"
    }
    ```

### 4. 【内部专用】异步解析处理接口 (Internal Ingestion Process)
由 Google Cloud Tasks 在安全环境后台回调，触发文本的实际提取、分块和向量落库。

- **HTTP Method**: `POST`
- **Path**: `/internal/api/v1/documents/process`
- **Headers**:
  - `Authorization: Bearer <OIDC_Token>` **（核心安全防线，严禁仅依赖请求头 X-Appengine-QueueName 鉴权）**
  - `X-Appengine-QueueName`: 仅作为辅助审计日志信息，不能作为主要安全依据。
- **安全拦截校验契约 (OIDC Token Validation)**：
  后端拦截器或 Endpoint 中间件必须对传入的 Authorization OIDC Token 执行标准验证：
  - **Signature 校验**：验证该 Token 的签名是否属于 Google 官方签发。
  - **Issuer 校验**：Issuer 必须等于 `https://accounts.google.com`。
  - **Audience 校验**：Audience (aud) 必须等于当前 Cloud Run 实例的公网绑定托管 URL 终点。
  - **Service Account 校验**：校验该 Token 绑定的主体身份（Email）确实是用于 Cloud Tasks 调用的安全服务账户。
- **Worker Payload 不可信防御原则**：
  - Cloud Tasks 回调 Body 中可能包含 `userId`、`documentId` 和 `gcsPath`。
  - **后端 Worker 严禁盲信传入的参数**。Worker 在收到任务后，必须先以 `documentId` 为唯一主键从 Firestore 数据库拉取最真实的 `document` 元数据。
  - **对比与校验逻辑**：
    1. 验证元数据中真实记录的 `userId` 与 Payload 传入的 `userId` 是否完全一致，防止越权干扰。
    2. 验证真正的物理文件 `gcsPath` 是否在格式和结构上完全符合 `users/{userId}/documents/{documentId}/` 的强多租户路径前缀。若不合规，拒绝处理并抛出安全审计警报。
    3. 验证元数据的 status：**只有当前状态等于 `"processing"` 的文档才允许进入后续的处理与向量计算**，防止二次污染、重复触发或被已逻辑删除的任务篡改。
- **Request Body**:
  ```json
  {
    "userId": "usr_abc123xyz",
    "documentId": "doc_8f7b2c9d1e4a3",
    "gcsPath": "gs://lifeos-user-knowledge-bucket/users/usr_abc123xyz/documents/doc_8f7b2c9d1e4a3/2026年骑行训练计划.pdf"
  }
  ```
- **Response**:
  - **Status Code**: `200 OK`
  - **Body**:
    ```json
    {
      "documentId": "doc_8f7b2c9d1e4a3",
      "status": "success",
      "processedChunksCount": 18
    }
    ```

---

## 三、 知识库 RAG 对话 API (RAG Chat)

### 1. 基于知识库检索的增强问答 (RAG Chat with Citations)
此接口通过 `conversationId` 拉取会话消息，并在完成检索后，将大模型的回答追加写入会话的 messages 子集合。

- **HTTP Method**: `POST`
- **Path**: `/api/v1/chat/rag`
- **Request Body**:
  ```json
  {
    "conversationId": "conv_9a2b3c4d", // 必填：用于从数据库中拉取最近 N 条历史记录（不建议前端直传 history）
    "message": "根据我的骑行计划，我下周二应该训练什么项目？", // 用户最新提问
    "clientTimeZone": "Asia/Shanghai", // 用于解释用户提问中的相对时间词（今天/昨天/下周二）
    "documentIds": ["doc_8f7b2c9d1e4a3"] // 可选：指定此数组可将 RAG 检索限制在特定的文档边界内
  }
  ```
- **Response**:
  - **Status Code**: `200 OK`
  - **Body**:
    ```json
    {
      "response": "根据您上传的《2026年骑行训练计划.pdf》，下周二您的训练项目是 **「18km 间歇有氧耐力骑行」** [1]。建议在骑行中平均心率控制在 140-150 bpm 区间 [2]。",
      "citationIntegrity": "valid", // 质量指标：valid, missing, partial, invalid_cleaned
      "citations": [
        {
          "index": 1,
          "documentId": "doc_8f7b2c9d1e4a3",
          "documentName": "2026年骑行训练计划.pdf",
          "chunkIndex": 3,
          "pageNumber": 2,
          "sectionTitle": "第二章：周度训练执行细则",
          "snippetPreview": "下周二（6月30日）：18km 间歇有氧耐力骑行，重点锻炼高心率下的乳酸耐受度..."
        },
        {
          "index": 2,
          "documentId": "doc_8f7b2c9d1e4a3",
          "documentName": "2026年骑行训练计划.pdf",
          "chunkIndex": 4,
          "pageNumber": 2,
          "sectionTitle": "第二章：周度训练执行细则",
          "snippetPreview": "心率控制规范：间歇骑行中，平均心率应控制在 140-150 bpm，切勿超过 165 bpm..."
        }
      ]
    }
    ```

---

## 四、 Firestore 原生 REST runQuery 候选路径表述降级

如果在双轨制方案中由于 SDK 接口不兼容需要切换到 REST API 查询，后端拟定发送的 REST API 目标路径为：

```http
POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery
```

> [!CAUTION]
> **【表述降级与技术 Spike 限制】**：
> 本路径仅作为初步选定的设计建议，**绝对不得视为未经技术验证前的最终正确路径**。
> **该路径必须在步骤 0 (S1 Spike) 中，通过本地 HttpClient 或 curl 命令行发送真实测试报文进行端到端实测验证**，重点校对父路径参数拼接、多租户子集合在 REST API 下的范围匹配度，并以此实测结果更新设计。

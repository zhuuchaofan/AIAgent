# Phase 3 Firestore 数据结构规范 (Schema Spec)

## 一、 数据隔离与查询原则

1. **绝对子集合隔离**：
   Phase 3 的 RAG 知识库元数据与向量分块，一律使用**子集合路径**进行严格的 UID 物理隔离（即 `users/{uid}/documents` 和 `users/{uid}/chunks`）。
   - 这继承了 Phase 1 & 2 的隔离设计，从底层防止了多租户越权访问。
   - `userId` 作为审计冗余存储于文档内，普通列表和检索不依赖 `userId`，路径天然完成此过滤。

2. **向量检索在子集合中的作用范围**：
   - nearest-neighbor 向量检索被限制在特定用户的子集合 `users/{uid}/chunks` 下进行。
   - 索引配置基于集合 ID（Collection ID）级别，范围为 Collection。这意味着对 `chunks` 集合建立一个向量索引后，能支持在任何具体用户的 `users/{userId}/chunks` 子集合中独立运行向量相似度匹配。

---

## 二、 集合与字段物理设计

### 1. 长期知识文档元数据 (users/{userId}/documents/{documentId})
用于追踪用户上传的源文件、大小、类型、上传及异步解析进度。

| 字段名 | 类型 | 必须 | 默认值 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `id` | String | 是 | - | 文档唯一 ID (即 `{documentId}`) |
| `userId` | String | 是 | - | 冗余审计字段：所属用户 ID |
| `fileName` | String | 是 | - | 用户上传的原始文件名 (如 "2026骑行训练.pdf") |
| `fileSize` | Integer | 是 | - | 文件大小，单位为字节 (Byte) |
| `mimeType` | String | 是 | - | 文件 MIME 格式，如 `application/pdf`, `text/plain` |
| `gcsPath` | String | 是 | - | 文件存储在 Cloud Storage 的物理 URL (如 `gs://bucket/users/uid/documents/doc_id/file.pdf`) |
| `status` | String | 是 | `"uploading"` | 状态枚举：`uploading`, `processing`, `deleting`, `success`, `failed` |
| `chunkCount` | Integer | 是 | `0` | 解析成功后，该文档被切片出的 Chunk 总数 |
| `errorMessage`| String | 否 | `null` | 若解析或向量化失败，记录脱敏后的故障原因简述 |
| `createdAt` | Timestamp| 是 | `ServerTimestamp` | 创建时间 |
| `updatedAt` | Timestamp| 是 | `ServerTimestamp` | 最后一次更新状态或数据的时间 |

---

### 2. 文档切片与向量数据 (users/{userId}/chunks/{chunkId})
存储切分后的文本块、相关的文档元数据，以及 768 维的向量（Embedding）。

| 字段名 | 类型 | 必须 | 说明 |
| :--- | :--- | :--- | :--- |
| `id` | String | 是 | 切片唯一 ID (即 `{chunkId}`)，采用 `doc_id + "_" + chunkIndex` 拼接以防冲突 |
| `userId` | String | 是 | 冗余审计字段：所属用户 ID |
| `documentId` | String | 是 | 关联的源文档 ID |
| `documentName` | String | 是 | 冗余字段：源文件名（直接用于组装 Citation 引用，免多余关联查询） |
| `chunkIndex` | Integer | 是 | 当前分块在该文档中的顺序索引（从 0 开始自增） |
| `pageNumber` | Integer | 是 | 对应的物理页码，PDF 格式有效（TXT / MD 默认存储为 1） |
| `sectionTitle` | String | 否 | 该分块所归属的最近的标题或小章节（提取不到时为 `null` 或空字符串） |
| `charStart` | Integer | 是 | 分块在原文中的字符起始索引 |
| `charEnd` | Integer | 是 | 分块在原文中的字符结束索引 |
| `content` | String | 是 | 提取切分出的文本分块原文段落 |
| `embedding` | VectorValue | 是 | **由 gemini-embedding-001 生成的 768 维向量值（显式指定 outputDimensionality = 768）。** 物理落库必须通过 REST commit 方式，以物理协议中 `mapValue` 封装的原生向量结构（标记 `__type__ = "__vector__"`）写入。读取时在 C# SDK 4.3.0 中会被反序列化为 `Dictionary<string, object>` |
| `createdAt` | Timestamp| 是 | 写入时间，继承对应的 document 创建时间 |

---

## 三、 Firestore 向量索引配置规范

要对 `embedding` 字段执行 **nearest-neighbor vector search**，必须明确在 Firestore 数据库中为子集合 `chunks` 的对应字段建立向量索引。

### 1. 向量索引属性
- **集合 ID (Collection ID)**: `chunks`
- **字段路径 (Field Path)**: `embedding`
- **查询范围 (Query Scope)**: `Collection` （子集合查询专用）
- **向量维度 (Dimension)**: **`768`** （必须与 `gemini-embedding-001` 的输出维度一致）
- **距离度量算法 (Distance Measure)**: **`COSINE` (余弦相似度)**

### 2. 向量索引配置 gcloud CLI 规范
若通过 Cloud CLI 命令行或配置脚本进行索引配置部署，可以使用以下声明（已在 Step 0 Spike 验证通过）：

```bash
gcloud alpha firestore indexes composite create \
    --collection-group=chunks \
    --query-scope=COLLECTION \
    --field-config=field-path=embedding,vector-config='{"dimension":"768","flat":{}}'
```

---

## 四、 向量检索主线 REST 终点规范（基于 Spike 实测结论）

由于高级 `.NET SDK (4.3.0)` 并不支持向量相关的强类型检索，本系统全面采用 `REST API` 进行最近邻检索：

* **请求 Endpoint URL**:
  ```http
  POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery
  ```

* **请求 Headers**:
  ```http
  Content-Type: application/json
  Authorization: Bearer {Google_Cloud_OAuth2_Access_Token_Or_ADC_Token}
  ```

* **安全与寻址校验（基于 [spike_report.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase3/spike/spike_report.md)）**：
  1. **多租户安全锁**：通过在 Endpoint 路径中显式拼装 `{userId}`，使查询被强力局限在特定用户的子树上，天然防范数据越权。
  2. **非递归查询**：在 structuredQuery 的 from 属性中声明 `collectionId = "chunks"` 并限制 `"allDescendants": false`。
  3. **距离字段定义**：在 findNearest 部分配置 `"distanceResultField": "vector_distance"`。在返回结果的 document 字段集中，系统将提取与普通字段平行的 `vector_distance` 双精度浮点数值（余弦距离），以便系统进一步过滤。

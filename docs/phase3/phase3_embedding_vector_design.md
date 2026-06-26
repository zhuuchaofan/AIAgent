# Phase 3 Embedding 与向量检索设计 (Embedding & Vector Search Design)

## 一、 Embedding 模型选型与输出维度规范

在 Phase 3 的知识接入层中，向量表达的精确度直接决定了检索匹配的质量。

### 1. 模型选型
- **默认模型**: **`gemini-embedding-001`**。
- **请求要求**：**在向 API 发送 Embedding 请求时，必须显式指定 `outputDimensionality = 768`**。
- **配置规则**: Firestore 复合向量索引创建时，其配置的维数必须同模型输出的 `768` 严格对齐。
- **后续调研项**：`gemini-embedding-2` 只作为后续技术调研与储备项，不作为 Phase 3 默认可切换项。

---

## 二、 检索相似度阈值设计 (Distance/Score Threshold)

为了控制噪声 Chunk 对大语言模型的干扰，防止其因阅读了不相关的文字导致回答发生严重幻觉，本方案引入了**配置化检索相似度阈值过滤机制**。

1. **度量指标（余弦相似度）**：
   在 768 维空间下，两个向量的余弦距离 (Cosine Distance) 的取值范围在 $[0, 2]$（其中 0 代表完全相同，2 代表完全相反）。相似度得分 (Similarity Score) 可简化定义为 $1.0 - \text{Cosine Distance}$。

2. **可配置过滤机制（Configurable Filter Threshold）**：
   - **动态配置化**：余弦距离 `0.35`（相似度得分下限 `0.65`）**仅作为 Phase 3 初始默认值，绝不得编码死为不可调整的常量**。设计上必须要求通过 .NET `appsettings.json`（例如 `RAG:DistanceThreshold` 属性）或环境变量进行动态注入。
   - **过滤逻辑**：从 Firestore 检索返回 of Top-K 向量分块中，系统必须动态比对。任何余弦距离超过配置门限（如 `0.35`）的分块**一律强行过滤拦截，不得喂给大模型**。
   - **空上下文降级**：如果所有检索到的 Chunks 均被阈值拦截，导致有效参考分块数为 0，系统立即判定为“未命中状态”，直接跳过大模型推理，直接向前端返回：“资料中未找到足够依据回答该问题。”，完成安全的空上下文降级。

3. **检索审计日志规范**：
   - 检索组件在执行向量相似度计算并得到 topK 结果后，**必须在日志（Cloud Logging）中详细记录每一个分块的实际 `distance` 和 `score`**（格式如：`Chunk ID: {id}, Distance: {distance}, Score: {score}, ConfigLimit: {limit}`）。
   - 这不仅提供了安全审计，也为生产环境下进行微调、阈值调参（例如在 0.3 到 0.4 之间取舍）提供了高精度的量化日志支撑。

---

## 三、 后续模型替换平滑迁移策略

如果在后续阶段我们需要升级模型，本系统制订了增量与全量重建的平滑迁移策略：
- **数据源拉取**：得益于 `documents` 集合中完整记录了源文件的 `gcsPath`，我们不需要保留历史向量的连续性，只需要根据 `gcsPath` 重新下载源物理文件，使用新模型重新分块计算向量。
- **双版本平滑切换**：可以在 Firestore 中将向量数据写入不同的集合（如 `chunks_v2`），或者在 chunks 结构中记录 `modelLabel`（例如 `"gemini-embedding-001"`）。在跑批重建期间，RAG Chat 接口自动对未重建完的用户采用老向量版本降级查询，全量重建完后再一键热切换，做到对用户 100% 零感知的平滑无损。

---

## 四、 .NET 检索实现：双轨制与 REST fallback 深度设计

为防范 `Google.Cloud.Firestore` SDK 强类型向量搜索在特定 runtime 下由于版本不兼容导致构建失败，系统采用**“双轨制”最近邻（nearest-neighbor）相似度检索设计**。

### 轨 A：Firestore .NET SDK 原生检索逻辑
若 SDK 版本和底层类库已就绪，首选高内聚实现：

```csharp
// 伪代码，展示 SDK 检索
public async Task<List<ChunkDocument>> SearchNearestAsync(string userId, double[] queryEmbedding, int limit = 5)
{
    var db = FirestoreDb.Create(_projectId);
    var chunksCollection = db.Collection("users").Document(userId).Collection("chunks");

    // 转化为 SDK 向量值
    var queryVector = VectorValue.Create(queryEmbedding.Select(v => (float)v).ToArray());

    // 使用 nearest-neighbor 机制在子集合内进行检索
    Query query = chunksCollection.FindNearest(
        "embedding",
        queryVector,
        limit,
        DistanceMeasure.Cosine,
        "vector_distance"
    );

    QuerySnapshot querySnapshot = await query.GetSnapshotAsync();
    // 转换为 C# 模型并返回...
}
```

---

### 轨 B：Firestore REST API `findNearest` 降级候选伪代码 (HttpClient)

若 C# 本地强类型 SDK 运行遇到阻碍，后端激活此 HttpClient Fallback 模块。

#### 1. REST 请求结构
- **Endpoint URL**: 
  ```
  POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery
  ```
- **请求 Headers**:
  ```http
  Content-Type: application/json
  Authorization: Bearer {Google_Cloud_OAuth2_Access_Token_Or_ADC_Token}
  ```

#### 2. REST 请求 Body Payload (StructuredQuery 候选格式)
```json
{
  "structuredQuery": {
    "from": [
      {
        "collectionId": "chunks",
        "allDescendants": false
      }
    ],
    "findNearest": {
      "vectorField": {
        "fieldPath": "embedding"
      },
      "queryVector": {
        "values": [
          0.0234, -0.0125, 0.0891, "...(共 768 个浮点数)"
        ]
      },
      "distanceMeasure": "COSINE",
      "limit": 5,
      "distanceResultField": "vector_distance"
    }
  }
}
```

#### 3. 局限性说明与快照测试保障 (Snapshot Testing)
> [!CAUTION]
> **【表述降级与 Spike 校验限制】**：
> 这里的 REST 实现方案（包括推荐路径 `POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery`）与 StructuredQuery Payload、返回结果 Parser **仅作为候选设计参考，在未经 S1 Spike 本地 HttpClient / curl 验证前，绝对不代表最终的绝对正确路径与报文格式**。
> 
> 在步骤 0 (S1 Spike) 中必须通过本地实测，细致校对和对齐：
> 1. **Parent Path 路径格式**：包含用户 `{userId}` 的父目录树路径是否能在 REST API 中正确寻址并实现多租户严格闭锁。
> 2. **From CollectionId**：验证 `collectionId: chunks` 对子集合的精确匹配表现。
> 3. **DistanceResultField**：确认 `distanceResultField` 距离返回值在 JSON payload 中返回的真实字段名。
> 4. **VectorValue 格式**：验证 768d 数组作为 `values` 节点在 REST 传输中无精度损失的打包格式。
> 5. **返回 JSON Parser**：确保后端解析复杂 JSON 嵌套树时，C# 的反序列化处理不会发生报错或字段截断。
>
> 在正式集成前，**必须基于实际 Firestore `runQuery` 返回的物理报文结构，组织自动化快照测试 (Snapshot Testing) 进行报文结构与字段解析的严格校验对齐**。防止由于 GCP 字段命名在 REST 格式下的细微多态导致运行时抛出解析异常。

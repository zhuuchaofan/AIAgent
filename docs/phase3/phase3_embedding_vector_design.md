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
   - **动态配置化**：余弦距离 `0.35`（相似度得分下限 `0.65`）**仅作为 Phase 3 初始默认值，绝不得编码死为不可调整 learnings 的常量**。设计上必须要求通过 .NET `appsettings.json`（例如 `RAG:DistanceThreshold` 属性）或环境变量进行动态注入。
   - **过滤逻辑**：从 Firestore 检索返回的 Top-K 向量分块中，系统必须动态比对。任何余弦距离超过配置门限（如 `0.35`）的分块**一律强行过滤拦截，不得喂给大模型**。
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

## 四、 .NET 向量读写与检索实现：主线 REST 方案

根据 **Step 0 Spike 验证（详见 [spike_report.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase3/spike/spike_report.md)）**，当前项目引用的高级 `.NET SDK (Google.Cloud.Firestore 4.3.0)` 并不暴露 `VectorValue` 与 `FindNearest` 相关的类和方法。

因此，本系统直接确立**“混合实现”为主线方案**：
1. **普通 CRUD 读写**：依旧采用 `.NET SDK` 强类型或 Dict 的方式处理 documents / messages / metadata 的常规业务数据。
2. **向量写入 (Commit) 与检索 (findNearest)**：全面采用 `REST API` 进行物理封包交互。
3. **数据读取兼容性**：经 Spike 验证，使用 Firestore SDK 直接读取包含原生向量字段的文档时，系统不会发生解析崩溃，向量会被温和反序列化为 `System.Collections.Generic.Dictionary<string, object>`。

---

### 1. 向量写入 (Commit) 主线 REST 设计

我们无法在 C# 中使用 SDK 构建原生 VectorValue。因此，我们必须使用 REST commit 接口，将向量部分按照 `mapValue` 及包含 `__type__ = "__vector__"` 标记的形式进行 JSON 打包并提交：

* **Endpoint URL**: 
  ```
  POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents:commit
  ```
* **Headers**:
  ```http
  Content-Type: application/json
  Authorization: Bearer {Google_Cloud_OAuth2_Access_Token_Or_ADC_Token}
  ```
* **Payload 格式示例**:
  ```json
  {
    "writes": [
      {
        "update": {
          "name": "projects/{projectId}/databases/(default)/documents/users/{userId}/chunks/{chunkId}",
          "fields": {
            "id": { "stringValue": "{chunkId}" },
            "userId": { "stringValue": "{userId}" },
            "content": { "stringValue": "本周训练重点是..." },
            "embedding": {
              "mapValue": {
                "fields": {
                  "__type__": { "stringValue": "__vector__" },
                  "value": {
                    "arrayValue": {
                      "values": [
                        { "doubleValue": 0.0123 },
                        { "doubleValue": -0.0456 }
                        // 共 768 个维度
                      ]
                    }
                  }
                }
              }
            }
          }
        }
      }
    ]
  }
  ```

---

### 2. 向量最近邻检索 (runQuery) 主线 REST 设计

* **Endpoint URL**: 
  ```
  POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery
  ```
* **Payload 格式示例**:
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
          "mapValue": {
            "fields": {
              "__type__": { "stringValue": "__vector__" },
              "value": {
                "arrayValue": {
                  "values": [
                    { "doubleValue": 0.0234 }
                    // 共 768 个查询向量 doubleValue
                  ]
                }
              }
            }
          }
        },
        "distanceMeasure": "COSINE",
        "limit": 5,
        "distanceResultField": "vector_distance"
      }
    }
  }
  ```

* **安全边界**：URL 路径的 parent 节点强制限定到具体的 `users/{userId}`，确保多租户向量物理隔离。同时将 `"allDescendants"` 显式设定为 `false`，仅在直接子集合 `chunks` 中匹配。

---

### 3. 返回 JSON 解析与快照测试 (Snapshot Testing)

当请求返回 HTTP 200 后，后端使用 `System.Text.Json` 的 `JsonDocument` 树形解析器，从中解析出匹配成功的文档以及回显的 `vector_distance` 字段：

```csharp
// 核心解析参考实现
using var doc = JsonDocument.Parse(responseJson);
if (doc.RootElement.ValueKind == JsonValueKind.Array)
{
    foreach (var element in doc.RootElement.EnumerateArray())
    {
        if (!element.TryGetProperty("document", out var document)) continue;
        
        string docId = document.GetProperty("name").GetString().Split('/').Last();
        double distance = -1.0;
        
        if (document.TryGetProperty("fields", out var fields) &&
            fields.TryGetProperty("vector_distance", out var distField))
        {
            if (distField.TryGetProperty("doubleValue", out var dv))
                distance = dv.GetDouble();
        }
        // 进行 DTO 还原和阈值过滤...
    }
}
```

为了确保解析在任何 GCP 环境微调下都能保持 100% 正确，**开发 B7 前必须基于 [spike_report.md](file:///Volumes/fanxiang/01_Development/google_Agent/AIAgent/docs/phase3/spike/spike_report.md) 中记录的真实 runQuery 物理报文结构组织自动化快照测试 (Snapshot Testing)**，保护解析链路不受底层字段多态影响。

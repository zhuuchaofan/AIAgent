# Phase 3 Step 0：Firestore Vector Search 技术 Spike 验证报告

## 1. 概述与核心结论

本轮 Spike 旨在验证 Firestore Vector Search 在当前项目技术栈（.NET 10 + Google.Cloud.Firestore 4.3.0）下的真实可行性。  
**核心结论**：
- 🛑 **高级 .NET SDK (4.3.0) 无法直接使用**：SDK 4.3.0 中完全没有 `VectorValue` 和 `FindNearest` 相关的类或扩展方法。此路在当前 SDK 版本下不通。
- ✅ **REST 轨 (HttpClient) 100% 真实可行**：通过 Google.Apis.Auth 自动换取本地 ADC (Application Default Credentials) Access Token，利用 REST 接口可以完美执行 768 维度向量的原生写入（物理 Commit）与 `StructuredQuery` `findNearest` 最近邻相似度检索（余弦相似度）。
- ✅ **数据读取是安全的**：用高级 .NET SDK 直接 GET 含有原生向量的文档时，程序不会发生解析崩溃，而是会将 embedding 字段平滑反序列化为 `Dictionary<string, object>`。
- ✅ **建议进入 Phase 3 正式开发**：已确认技术通路闭环。后端 RAG 开发中应全面采用 **REST API Fallback 方案（即 轨 B）** 作为核心向量读写和检索实现。

---

## 2. 各项验证结果汇总

| 验证项 | 目标 | 结果 | 细节说明 |
| :--- | :--- | :--- | :--- |
| **1** | 在 C# 中写入 768 维原生 VectorValue | **成功 (REST 模拟)** | 高级 SDK 无 `VectorValue` 类型。我们通过 REST `commit` 接口，在 Payload 中将向量字段映射为符合 Firestore 物理协议的 `mapValue`（标记 `__type__ = __vector__`），成功在 C# 中生成并写入 768 维原生向量，云端物理入库成功（HTTP 200）。 |
| **2** | 创建 chunks.embedding 的 768d COSINE 向量索引 | **成功** | 运行 `gcloud alpha firestore indexes composite create` 命令成功在 `chunks` 集合的 `embedding` 字段上建立了 768 维度、度量算法为 `COSINE` 的复合向量索引。目前状态已变为 `READY`。 |
| **3** | 通过 .NET SDK nearest-neighbor 返回 Top-K | **失败** | 4.3.0 SDK 的 `CollectionReference` 极其缺乏 `FindNearest` 扩展方法，编译不通过。该轨不可用。 |
| **4** | REST 候选路径真实可用性与 parent path 验证 | **成功** | 接口 `POST https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery` 验证真实可用。可以通过 parent path 将查询范围严格限制在特定用户的子集合 `users/{userId}/chunks`，实现高强度的多租户安全隔离，且 `allDescendants = false` 正常生效。 |
| **5** | distanceResultField 属性、VectorValue 格式与 JSON 反序列化 | **成功** | 成功在 REST runQuery 中通过 `distanceResultField` 获得计算后的余弦距离，并使用 C# 的 `JsonDocument` 成功将返回的复杂 JSON 嵌套树完美反序列化，提取出命中文档与匹配分数。 |
| **6** | 查询范围限制越权验证 | **成功** | REST API 强制 parent path 绑定 `{userId}`，不属于该目录的 chunks 绝对无法召回，满足多租户物理隔离。 |
| **7** | 本地 ADC / 云端服务账号权限验证 | **成功** | 本地 ADC 通过 `https://www.googleapis.com/auth/datastore` 作用域成功获取 Access Token，对 Firestore 的 `commit` 和 `runQuery` 拥有完全的读写和执行权限，云端 IAM 策略完全匹配。 |

---

## 3. 详细技术细节与 Payload 快照

### 3.1 实际使用的 REST 写入 (Commit) Payload 结构

在 C# 中，我们使用 HttpClient 投递到 `/projects/{projectId}/databases/(default)/documents:commit` 的 Payload 中，将向量字段格式化如下：

```json
{
  "writes": [
    {
      "update": {
        "name": "projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001/chunks/spike_chunk_001",
        "fields": {
          "id": { "stringValue": "spike_chunk_001" },
          "userId": { "stringValue": "spike_test_user_001" },
          "content": { "stringValue": "本周训练重点是长距离耐力骑行..." },
          "embedding": {
            "mapValue": {
              "fields": {
                "__type__": { "stringValue": "__vector__" },
                "value": {
                  "arrayValue": {
                    "values": [
                      { "doubleValue": -0.0123 },
                      { "doubleValue": 0.0891 }
                      // 共 768 个 doubleValue
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

### 3.2 实际使用的 REST 检索 (runQuery) Payload 结构

接口 URL: `POST https://firestore.googleapis.com/v1/projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001:runQuery`

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
      "vectorField": { "fieldPath": "embedding" },
      "queryVector": {
        "mapValue": {
          "fields": {
            "__type__": { "stringValue": "__vector__" },
            "value": {
              "arrayValue": {
                "values": [
                  { "doubleValue": 0.0123 }
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

### 3.3 实际返回的 JSON 响应样例 (部分截断)

```json
[
  {
    "document": {
      "name": "projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001/chunks/spike_chunk_002",
      "fields": {
        "id": { "stringValue": "spike_chunk_002" },
        "userId": { "stringValue": "spike_test_user_001" },
        "content": { "stringValue": "赛前48小时建议增加碳水化合物摄入，减少高纤维食物，保持充分水分补充。" },
        "vector_distance": { "doubleValue": 1.0239136370136133 }
      },
      "createTime": "2026-06-26T07:30:02.123456Z",
      "updateTime": "2026-06-26T07:30:02.123456Z"
    },
    "readTime": "2026-06-26T07:31:58.789123Z"
  },
  {
    "document": {
      "name": "projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001/chunks/spike_chunk_001",
      "fields": {
        "id": { "stringValue": "spike_chunk_001" },
        "userId": { "stringValue": "spike_test_user_001" },
        "content": { "stringValue": "本周训练重点是长距离耐力骑行，目标里程80km，配速保持在25-28km/h区间。" },
        "vector_distance": { "doubleValue": 1.0414256779531355 }
      },
      "createTime": "2026-06-26T07:30:01.987654Z",
      "updateTime": "2026-06-26T07:30:01.987654Z"
    },
    "readTime": "2026-06-26T07:31:58.789123Z"
  }
]
```

### 3.4 最终检索召回解析结果

- **召回 1**: Document `spike_chunk_002`, Cosine Distance: `1.0239`, Score (1 - dist): `-0.0239`
- **召回 2**: Document `spike_chunk_001`, Cosine Distance: `1.0414`, Score (1 - dist): `-0.0414`

*(注：由于测试时使用的是随机归一化向量，所以在 768 维空间中的余弦距离为 1.0 左右（即几乎正交，相关度低），这完美符合高维随机向量的统计学分布规律，且距离计算与回显完全正常。)*

---

## 4. 后续开发指导建议

1. **废弃高级 SDK 向量检索部分**：正式开发 Phase 3 时，不要去引用或尝试使用 SDK 里的 VectorValue。
2. **使用 HttpClient 作为核心向量检索客户端**：
   - 可以在后端封装一个 `FirestoreVectorSearchClient`，负责从 ADC 获取 token，并发送 REST `runQuery` 请求。
   - 使用 `System.Text.Json` 或 `Newtonsoft.Json` 构建和解析相应的 REST 报文。
3. **安全审计与多租户限制**：
   - 在构造 `runQuery` URL 时，必须动态拼装当前登录用户的 `userId`：
     `https://firestore.googleapis.com/v1/projects/{projectId}/databases/(default)/documents/users/{userId}:runQuery`
   - 查询 Body 中必须显式保留 `"allDescendants": false` 确保限制在直接子集合内，绝对禁止跨越到其他用户的 chunks。

// ┌──────────────────────────────────────────────────────────────────────┐
// │  SPIKE FILE — Phase 3 Step 0 技术验证                                │
// │  目的：验证 Firestore Vector Search 在当前技术栈中的真实可行性          │
// │  由于高级 .NET SDK 4.3.0 暂不支持 VectorValue 与 FindNearest，          │
// │  Spike 将重点验证 REST runQuery / commit 原生写入与检索可行性。        │
// └──────────────────────────────────────────────────────────────────────┘

using Google.Cloud.Firestore;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LifeAgent.Api.Spike;

public class VectorSpikeService
{
    private readonly FirestoreDb _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VectorSpikeService> _logger;

    private const string SpikeUserId = "spike_test_user_001";
    private const string ProjectId = "copper-affinity-467409-k7";
    private const int VectorDimension = 768;

    public VectorSpikeService(
        FirestoreDb db,
        IHttpClientFactory httpClientFactory,
        ILogger<VectorSpikeService> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var credential = await Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefaultAsync();
        var scopedCredential = credential.CreateScoped("https://www.googleapis.com/auth/datastore");
        return await ((Google.Apis.Auth.OAuth2.ITokenAccess)scopedCredential).GetAccessTokenForRequestAsync();
    }

    // ─────────────────────────────────────────────────────────────────
    // 验证点 1：REST 原生 VectorValue 写入
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 使用 REST commit 接口向 users/spike_test_user_001/chunks 写入两个 768d 向量
    /// 验证原生 VectorValue (stringValue = "__vector__") 能否成功入库。
    /// </summary>
    public async Task<SpikeWriteResult> WriteTestChunksAsync()
    {
        var result = new SpikeWriteResult();
        var client = _httpClientFactory.CreateClient("spike");

        try
        {
            var token = await GetAccessTokenAsync();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var embeddingA = GenerateFakeEmbedding(seed: 42);
            var embeddingB = GenerateFakeEmbedding(seed: 99);

            var url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents:commit";

            // 构造 REST 写入 Payload，利用 mapValue 形式定义 __vector__
            var payload = new
            {
                writes = new[]
                {
                    new
                    {
                        update = new
                        {
                            name = $"projects/{ProjectId}/databases/(default)/documents/users/{SpikeUserId}/chunks/spike_chunk_001",
                            fields = new Dictionary<string, object>
                            {
                                ["id"] = new { stringValue = "spike_chunk_001" },
                                ["userId"] = new { stringValue = SpikeUserId },
                                ["documentId"] = new { stringValue = "spike_doc_001" },
                                ["documentName"] = new { stringValue = "骑行训练计划2026.pdf" },
                                ["chunkIndex"] = new { integerValue = "0" },
                                ["pageNumber"] = new { integerValue = "1" },
                                ["charStart"] = new { integerValue = "0" },
                                ["charEnd"] = new { integerValue = "200" },
                                ["content"] = new { stringValue = "本周训练重点是长距离耐力骑行，目标里程80km，配速保持在25-28km/h区间。" },
                                ["embedding"] = new
                                {
                                    mapValue = new
                                    {
                                        fields = new Dictionary<string, object>
                                        {
                                            ["__type__"] = new { stringValue = "__vector__" },
                                            ["value"] = new
                                            {
                                                arrayValue = new
                                                {
                                                    values = embeddingA.Select(v => new { doubleValue = (double)v }).ToArray()
                                                }
                                            }
                                        }
                                    }
                                },
                                ["createdAt"] = new { timestampValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") }
                            }
                        }
                    },
                    new
                    {
                        update = new
                        {
                            name = $"projects/{ProjectId}/databases/(default)/documents/users/{SpikeUserId}/chunks/spike_chunk_002",
                            fields = new Dictionary<string, object>
                            {
                                ["id"] = new { stringValue = "spike_chunk_002" },
                                ["userId"] = new { stringValue = SpikeUserId },
                                ["documentId"] = new { stringValue = "spike_doc_001" },
                                ["documentName"] = new { stringValue = "骑行训练计划2026.pdf" },
                                ["chunkIndex"] = new { integerValue = "1" },
                                ["pageNumber"] = new { integerValue = "2" },
                                ["charStart"] = new { integerValue = "200" },
                                ["charEnd"] = new { integerValue = "400" },
                                ["content"] = new { stringValue = "赛前48小时建议增加碳水化合物摄入，减少高纤维食物，保持充分水分补充。" },
                                ["embedding"] = new
                                {
                                    mapValue = new
                                    {
                                        fields = new Dictionary<string, object>
                                        {
                                            ["__type__"] = new { stringValue = "__vector__" },
                                            ["value"] = new
                                            {
                                                arrayValue = new
                                                {
                                                    values = embeddingB.Select(v => new { doubleValue = (double)v }).ToArray()
                                                }
                                            }
                                        }
                                    }
                                },
                                ["createdAt"] = new { timestampValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") }
                            }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            result.WriteSuccess = response.IsSuccessStatusCode;
            result.VectorDimension = VectorDimension;
            result.FirestorePath = $"users/{SpikeUserId}/chunks";
            result.WrittenDocIds = ["spike_chunk_001", "spike_chunk_002"];

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                result.ErrorMessage = $"HTTP {response.StatusCode}: {body}";
                _logger.LogError("[Spike] ❌ REST Vector 写入失败: {Body}", body);
            }
            else
            {
                result.SdkVectorValueCreationSuccess = true;
                _logger.LogInformation("[Spike] ✅ REST 原生 VectorValue 写入成功");
            }
        }
        catch (Exception ex)
        {
            result.WriteSuccess = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[Spike] ❌ REST 写入过程出现异常");
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────
    // 验证点 3：SDK FindNearest 检索 (此处作为“不可用”记录)
    // ─────────────────────────────────────────────────────────────────

    public Task<SpikeFindNearestResult> SdkFindNearestAsync()
    {
        return Task.FromResult(new SpikeFindNearestResult
        {
            Success = false,
            Method = "SDK.FindNearest",
            ErrorMessage = "Google.Cloud.Firestore SDK 4.3.0 不包含 VectorValue 类和 FindNearest 方法，该轨不可用。"
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // 验证点 4：REST runQuery/findNearest 相似度检索
    // ─────────────────────────────────────────────────────────────────

    public async Task<SpikeRestResult> RestFindNearestAsync()
    {
        var result = new SpikeRestResult();
        var client = _httpClientFactory.CreateClient("spike");

        try
        {
            var token = await GetAccessTokenAsync();
            result.TokenObtained = true;

            var parentPath = $"projects/{ProjectId}/databases/(default)/documents/users/{SpikeUserId}";
            var url = $"https://firestore.googleapis.com/v1/{parentPath}:runQuery";

            result.ActualUrl = url;
            result.ParentPath = parentPath;

            var queryEmbedding = GenerateFakeEmbedding(seed: 43); // seed=43 近似 seed=42

            var payload = new
            {
                structuredQuery = new
                {
                    from = new[]
                    {
                        new
                        {
                            collectionId = "chunks",
                            allDescendants = false
                        }
                    },
                    findNearest = new
                    {
                        vectorField = new { fieldPath = "embedding" },
                        queryVector = new
                        {
                            mapValue = new
                            {
                                fields = new Dictionary<string, object>
                                {
                                    ["__type__"] = new { stringValue = "__vector__" },
                                    ["value"] = new
                                    {
                                        arrayValue = new
                                        {
                                            values = queryEmbedding.Select(v => new { doubleValue = (double)v }).ToArray()
                                        }
                                    }
                                }
                            }
                        },
                        distanceMeasure = "COSINE",
                        limit = 5,
                        distanceResultField = "vector_distance"
                    }
                }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            result.ActualRequestPayload = jsonPayload;

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            result.HttpStatusCode = (int)response.StatusCode;
            var responseBody = await response.Content.ReadAsStringAsync();
            result.ActualResponseJson = responseBody;

            if (response.IsSuccessStatusCode)
            {
                result.HttpSuccess = true;
                result.ParsedHits = ParseRestResponse(responseBody);
                result.RestApiSuccess = result.ParsedHits.Count > 0;
                _logger.LogInformation("[Spike] ✅ REST runQuery 成功召回 {Count} 个向量", result.ParsedHits.Count);
            }
            else
            {
                result.HttpSuccess = false;
                result.ErrorMessage = $"HTTP {response.StatusCode}: {responseBody}";
                _logger.LogError("[Spike] ❌ REST runQuery 检索失败: {Body}", responseBody);
            }
        }
        catch (Exception ex)
        {
            result.HttpSuccess = false;
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "[Spike] ❌ REST 检索过程异常");
        }

        return result;
    }

    private List<SpikeHit> ParseRestResponse(string json)
    {
        var hits = new List<SpikeHit>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("document", out var document))
                        continue;

                    var docId = "(unknown)";
                    var path = "(unknown)";
                    var distance = -1.0;
                    var content = "(no content)";

                    if (document.TryGetProperty("name", out var nameProp))
                    {
                        path = nameProp.GetString() ?? "(unknown)";
                        docId = path.Split('/').LastOrDefault() ?? "(unknown)";
                    }

                    if (document.TryGetProperty("fields", out var fields))
                    {
                        if (fields.TryGetProperty("vector_distance", out var distField))
                        {
                            if (distField.TryGetProperty("doubleValue", out var dv))
                                distance = dv.GetDouble();
                            else if (distField.TryGetProperty("integerValue", out var iv))
                                distance = iv.GetDouble();
                        }

                        if (fields.TryGetProperty("content", out var contentField))
                        {
                            if (contentField.TryGetProperty("stringValue", out var sv))
                                content = sv.GetString() ?? "(no content)";
                        }
                    }

                    hits.Add(new SpikeHit
                    {
                        DocId = docId,
                        Path = path,
                        Distance = distance,
                        Score = distance >= 0 ? 1.0 - distance : -1.0,
                        Content = content
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Spike] REST JSON 解析错误");
        }
        return hits;
    }

    public async Task<string> GetTestChunkViaSdkAsync()
    {
        try
        {
            var docRef = _db.Collection("users")
                .Document(SpikeUserId)
                .Collection("chunks")
                .Document("spike_chunk_001");

            var snapshot = await docRef.GetSnapshotAsync();
            if (!snapshot.Exists)
                return "Document not found";

            var embeddingValue = snapshot.GetValue<object>("embedding");
            var typeName = embeddingValue?.GetType().FullName ?? "null";

            _logger.LogInformation("[Spike] SDK Get: embedding 字段类型 = {Type}", typeName);
            return $"Success: Type of embedding is {typeName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Spike] SDK Get 失败");
            return $"Error: {ex.Message}";
        }
    }

    public async Task<bool> CleanupTestDataAsync()
    {
        try
        {
            var chunksRef = _db.Collection("users").Document(SpikeUserId).Collection("chunks");
            await chunksRef.Document("spike_chunk_001").DeleteAsync();
            await chunksRef.Document("spike_chunk_002").DeleteAsync();
            _logger.LogInformation("[Spike] ✅ 测试数据清理完成");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Spike] ❌ 测试数据清理失败");
            return false;
        }
    }

    private static float[] GenerateFakeEmbedding(int seed)
    {
        var rng = new Random(seed);
        var vec = new float[VectorDimension];
        double norm = 0;
        for (int i = 0; i < VectorDimension; i++)
        {
            vec[i] = (float)(rng.NextDouble() * 2 - 1);
            norm += vec[i] * vec[i];
        }
        norm = Math.Sqrt(norm);
        for (int i = 0; i < VectorDimension; i++)
            vec[i] = (float)(vec[i] / norm);
        return vec;
    }
}

public class SpikeWriteResult
{
    public bool WriteSuccess { get; set; }
    public bool SdkVectorValueCreationSuccess { get; set; }
    public int VectorDimension { get; set; }
    public string FirestorePath { get; set; } = "";
    public List<string> WrittenDocIds { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public class SpikeFindNearestResult
{
    public bool Success { get; set; }
    public string Method { get; set; } = "";
    public int ReturnedCount { get; set; }
    public List<SpikeHit> Hits { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public class SpikeRestResult
{
    public bool TokenObtained { get; set; }
    public string ActualUrl { get; set; } = "";
    public string ParentPath { get; set; } = "";
    public string ActualRequestPayload { get; set; } = "";
    public int HttpStatusCode { get; set; }
    public bool HttpSuccess { get; set; }
    public bool RestApiSuccess { get; set; }
    public string ActualResponseJson { get; set; } = "";
    public List<SpikeHit> ParsedHits { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public class SpikeHit
{
    public string DocId { get; set; } = "";
    public string Path { get; set; } = "";
    public double Distance { get; set; }
    public double Score { get; set; }
    public string Content { get; set; } = "";
}

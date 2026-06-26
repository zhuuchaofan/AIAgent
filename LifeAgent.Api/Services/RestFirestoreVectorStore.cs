using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class RestFirestoreVectorStore : IFirestoreVectorStore
{
    private readonly HttpClient _httpClient;
    private readonly FirestoreDb _db;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<RestFirestoreVectorStore> _logger;
    private readonly string _projectId;

    public RestFirestoreVectorStore(
        HttpClient httpClient,
        FirestoreDb db,
        IOptions<RagOptions> ragOptions,
        IConfiguration config,
        ILogger<RestFirestoreVectorStore> logger)
    {
        _httpClient = httpClient;
        _db = db;
        _ragOptions = ragOptions.Value;
        _logger = logger;
        _projectId = config["Firestore:ProjectId"] ?? "copper-affinity-467409-k7";
    }

    private async Task<string> GetAccessTokenAsync()
    {
        var credential = await Google.Apis.Auth.OAuth2.GoogleCredential.GetApplicationDefaultAsync();
        var scopedCredential = credential.CreateScoped("https://www.googleapis.com/auth/datastore");
        return await ((Google.Apis.Auth.OAuth2.ITokenAccess)scopedCredential).GetAccessTokenForRequestAsync();
    }

    public async Task WriteChunksAsync(string userId, List<KnowledgeChunk> chunks, List<float[]> embeddings)
    {
        if (chunks == null || chunks.Count == 0) return;
        if (embeddings == null || embeddings.Count != chunks.Count)
        {
            throw new ArgumentException("Chunks and embeddings count must match.");
        }

        var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents:commit";
        var payloadJson = BuildCommitPayload(_projectId, userId, chunks, embeddings);
        
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        var token = await GetAccessTokenAsync();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Firestore REST Commit failed: {Status} - {Error}", response.StatusCode, errorContent);
            throw new HttpRequestException($"Firestore REST Commit failed with status {response.StatusCode}: {errorContent}");
        }

        _logger.LogInformation("Successfully wrote {Count} chunks with embeddings via REST Commit", chunks.Count);
    }

    public async Task<List<VectorSearchResult>> FindNearestAsync(string userId, float[] queryVector, int limit)
    {
        if (queryVector == null || queryVector.Length == 0)
        {
            return new List<VectorSearchResult>();
        }

        var parentPath = $"projects/{_projectId}/databases/(default)/documents/users/{userId}";
        var url = $"https://firestore.googleapis.com/v1/{parentPath}:runQuery";
        var payloadJson = BuildRunQueryPayload(queryVector, limit);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        var token = await GetAccessTokenAsync();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Firestore REST runQuery failed: {Status} - {Error}", response.StatusCode, errorContent);
            throw new HttpRequestException($"Firestore REST runQuery failed with status {response.StatusCode}: {errorContent}");
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var allResults = ParseRunQueryResponse(jsonResponse);

        var threshold = _ragOptions.DistanceThreshold;
        var filteredResults = new List<VectorSearchResult>();

        foreach (var result in allResults)
        {
            // 记录 topK distance / score 审计日志
            // 规范格式: Chunk ID: {id}, Distance: {distance}, Score: {score}, ConfigLimit: {limit}
            _logger.LogInformation("Chunk ID: {Id}, Distance: {Distance}, Score: {Score}, ConfigLimit: {Limit}",
                result.Chunk.Id, result.Distance, result.Score, threshold);

            // 余弦距离必须小于等于设定的阈值，才是相关性高的结果
            if (result.Distance <= threshold)
            {
                filteredResults.Add(result);
            }
        }

        return filteredResults;
    }

    public async Task DeleteChunksByDocumentIdAsync(string userId, string documentId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(documentId))
        {
            return;
        }

        var chunksRef = _db.Collection("users").Document(userId).Collection("chunks");
        var query = chunksRef.WhereEqualTo("documentId", documentId);
        var snapshot = await query.GetSnapshotAsync();

        if (snapshot.Count == 0)
        {
            return;
        }

        var batch = _db.StartBatch();
        int count = 0;
        foreach (var doc in snapshot.Documents)
        {
            batch.Delete(doc.Reference);
            count++;
            if (count >= 500)
            {
                await batch.CommitAsync();
                batch = _db.StartBatch();
                count = 0;
            }
        }
        if (count > 0)
        {
            await batch.CommitAsync();
        }

        _logger.LogInformation("Deleted {Count} legacy chunks for document {DocId}", snapshot.Count, documentId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Payload 组装与解析静态辅助方法 (便于单元测试)
    // ────────────────────────────────────────────────────────────────────────

    public static string BuildCommitPayload(string projectId, string userId, List<KnowledgeChunk> chunks, List<float[]> embeddings)
    {
        var writesList = new List<object>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var embedding = embeddings[i];

            var fields = new Dictionary<string, object>
            {
                ["id"] = new { stringValue = chunk.Id },
                ["userId"] = new { stringValue = userId },
                ["documentId"] = new { stringValue = chunk.DocumentId },
                ["documentName"] = new { stringValue = chunk.DocumentName },
                ["chunkIndex"] = new { integerValue = chunk.ChunkIndex.ToString() },
                ["pageNumber"] = new { integerValue = chunk.PageNumber.ToString() },
                ["charStart"] = new { integerValue = chunk.CharStart.ToString() },
                ["charEnd"] = new { integerValue = chunk.CharEnd.ToString() },
                ["content"] = new { stringValue = chunk.Content },
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
                                    values = embedding.Select(v => new { doubleValue = (double)v }).ToArray()
                                }
                            }
                        }
                    }
                },
                ["createdAt"] = new { timestampValue = chunk.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") }
            };

            if (!string.IsNullOrEmpty(chunk.SectionTitle))
            {
                fields["sectionTitle"] = new { stringValue = chunk.SectionTitle };
            }

            var writeElement = new
            {
                update = new
                {
                    name = $"projects/{projectId}/databases/(default)/documents/users/{userId}/chunks/{chunk.Id}",
                    fields = fields
                }
            };

            writesList.Add(writeElement);
        }

        var payload = new { writes = writesList };
        return JsonSerializer.Serialize(payload);
    }

    public static string BuildRunQueryPayload(float[] queryVector, int limit)
    {
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
                                        values = queryVector.Select(v => new { doubleValue = (double)v }).ToArray()
                                    }
                                }
                            }
                        }
                    },
                    distanceMeasure = "COSINE",
                    limit = limit,
                    distanceResultField = "vector_distance"
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    public static List<VectorSearchResult> ParseRunQueryResponse(string jsonResponse)
    {
        var list = new List<VectorSearchResult>();
        if (string.IsNullOrEmpty(jsonResponse))
            return list;

        try
        {
            using var doc = JsonDocument.Parse(jsonResponse);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    if (!element.TryGetProperty("document", out var document))
                        continue;

                    var chunk = new KnowledgeChunk();
                    double distance = -1.0;

                    if (document.TryGetProperty("name", out var nameProp))
                    {
                        var nameVal = nameProp.GetString();
                        if (!string.IsNullOrEmpty(nameVal))
                        {
                            chunk.Id = nameVal.Split('/').LastOrDefault() ?? "";
                        }
                    }

                    if (document.TryGetProperty("fields", out var fields))
                    {
                        if (fields.TryGetProperty("id", out var idF) && idF.TryGetProperty("stringValue", out var idV))
                            chunk.Id = idV.GetString() ?? chunk.Id;

                        if (fields.TryGetProperty("userId", out var uidF) && uidF.TryGetProperty("stringValue", out var uidV))
                            chunk.UserId = uidV.GetString() ?? "";

                        if (fields.TryGetProperty("documentId", out var docIdF) && docIdF.TryGetProperty("stringValue", out var docIdV))
                            chunk.DocumentId = docIdV.GetString() ?? "";

                        if (fields.TryGetProperty("documentName", out var docNameF) && docNameF.TryGetProperty("stringValue", out var docNameV))
                            chunk.DocumentName = docNameV.GetString() ?? "";

                        if (fields.TryGetProperty("content", out var contentF) && contentF.TryGetProperty("stringValue", out var contentV))
                            chunk.Content = contentV.GetString() ?? "";

                        if (fields.TryGetProperty("chunkIndex", out var chunkIdxF))
                        {
                            if (chunkIdxF.TryGetProperty("integerValue", out var chunkIdxV) && int.TryParse(chunkIdxV.GetString(), out var val))
                                chunk.ChunkIndex = val;
                            else if (chunkIdxF.TryGetProperty("doubleValue", out var chunkIdxVal))
                                chunk.ChunkIndex = (int)chunkIdxVal.GetDouble();
                        }

                        if (fields.TryGetProperty("pageNumber", out var pageNumF))
                        {
                            if (pageNumF.TryGetProperty("integerValue", out var pageNumV) && int.TryParse(pageNumV.GetString(), out var val))
                                chunk.PageNumber = val;
                            else if (pageNumF.TryGetProperty("doubleValue", out var pageNumVal))
                                chunk.PageNumber = (int)pageNumVal.GetDouble();
                        }

                        if (fields.TryGetProperty("sectionTitle", out var secTitleF) && secTitleF.TryGetProperty("stringValue", out var secTitleV))
                            chunk.SectionTitle = secTitleV.GetString();

                        if (fields.TryGetProperty("charStart", out var charStartF))
                        {
                            if (charStartF.TryGetProperty("integerValue", out var charStartV) && int.TryParse(charStartV.GetString(), out var val))
                                chunk.CharStart = val;
                            else if (charStartF.TryGetProperty("doubleValue", out var charStartVal))
                                chunk.CharStart = (int)charStartVal.GetDouble();
                        }

                        if (fields.TryGetProperty("charEnd", out var charEndF))
                        {
                            if (charEndF.TryGetProperty("integerValue", out var charEndV) && int.TryParse(charEndV.GetString(), out var val))
                                chunk.CharEnd = val;
                            else if (charEndF.TryGetProperty("doubleValue", out var charEndVal))
                                chunk.CharEnd = (int)charEndVal.GetDouble();
                        }

                        if (fields.TryGetProperty("vector_distance", out var distField))
                        {
                            if (distField.TryGetProperty("doubleValue", out var dv))
                                distance = dv.GetDouble();
                            else if (distField.TryGetProperty("integerValue", out var iv) && double.TryParse(iv.GetString(), out var val))
                                distance = val;
                        }
                    }

                    list.Add(new VectorSearchResult
                    {
                        Chunk = chunk,
                        Distance = distance
                    });
                }
            }
        }
        catch (Exception)
        {
            // 鲁棒性防崩：解析格式不正确时吞掉或不抛出
        }

        return list;
    }
}

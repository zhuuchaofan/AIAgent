using System.Text.Json;
using Xunit;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Tests;

public class RestFirestoreVectorStoreTest
{
    [Fact]
    public void BuildCommitPayload_ShouldContainVectorTypeAndAllFields()
    {
        // Arrange
        var projectId = "test-project";
        var userId = "user-123";
        var chunks = new List<KnowledgeChunk>
        {
            new KnowledgeChunk
            {
                Id = "chunk-1",
                DocumentId = "doc-1",
                DocumentName = "doc1.txt",
                ChunkIndex = 0,
                PageNumber = 1,
                CharStart = 0,
                CharEnd = 100,
                Content = "This is chunk content",
                SectionTitle = "Section 1"
            }
        };

        var embeddings = new List<float[]>();
        var vector = new float[768];
        vector[0] = 0.5f;
        vector[767] = -0.5f;
        embeddings.Add(vector);

        // Act
        var payloadJson = RestFirestoreVectorStore.BuildCommitPayload(projectId, userId, chunks, embeddings);

        // Assert
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        
        Assert.True(root.TryGetProperty("writes", out var writes));
        Assert.Equal(JsonValueKind.Array, writes.ValueKind);
        Assert.Single(writes.EnumerateArray());

        var update = writes[0].GetProperty("update");
        Assert.Equal("projects/test-project/databases/(default)/documents/users/user-123/chunks/chunk-1", update.GetProperty("name").GetString());

        var fields = update.GetProperty("fields");
        Assert.Equal("chunk-1", fields.GetProperty("id").GetProperty("stringValue").GetString());
        Assert.Equal("user-123", fields.GetProperty("userId").GetProperty("stringValue").GetString());
        Assert.Equal("doc-1", fields.GetProperty("documentId").GetProperty("stringValue").GetString());
        Assert.Equal("doc1.txt", fields.GetProperty("documentName").GetProperty("stringValue").GetString());
        Assert.Equal("This is chunk content", fields.GetProperty("content").GetProperty("stringValue").GetString());
        Assert.Equal("0", fields.GetProperty("chunkIndex").GetProperty("integerValue").GetString());
        Assert.Equal("1", fields.GetProperty("pageNumber").GetProperty("integerValue").GetString());
        Assert.Equal("0", fields.GetProperty("charStart").GetProperty("integerValue").GetString());
        Assert.Equal("100", fields.GetProperty("charEnd").GetProperty("integerValue").GetString());
        Assert.Equal("Section 1", fields.GetProperty("sectionTitle").GetProperty("stringValue").GetString());

        var embedding = fields.GetProperty("embedding").GetProperty("mapValue").GetProperty("fields");
        Assert.Equal("__vector__", embedding.GetProperty("__type__").GetProperty("stringValue").GetString());
        
        var values = embedding.GetProperty("value").GetProperty("arrayValue").GetProperty("values");
        Assert.Equal(JsonValueKind.Array, values.ValueKind);
        Assert.Equal(768, values.GetArrayLength());
        Assert.Equal(0.5, values[0].GetProperty("doubleValue").GetDouble(), 5);
        Assert.Equal(-0.5, values[767].GetProperty("doubleValue").GetDouble(), 5);
    }

    [Fact]
    public void BuildRunQueryPayload_ShouldContainFindNearestCosineAndCorrectLimit()
    {
        // Arrange
        var queryVector = new float[768];
        queryVector[0] = 0.1f;
        queryVector[767] = 0.9f;
        var limit = 5;

        // Act
        var payloadJson = RestFirestoreVectorStore.BuildRunQueryPayload(queryVector, limit);

        // Assert
        using var doc = JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("structuredQuery", out var structuredQuery));
        var from = structuredQuery.GetProperty("from");
        Assert.Equal("chunks", from[0].GetProperty("collectionId").GetString());
        Assert.False(from[0].GetProperty("allDescendants").GetBoolean());

        var findNearest = structuredQuery.GetProperty("findNearest");
        Assert.Equal("embedding", findNearest.GetProperty("vectorField").GetProperty("fieldPath").GetString());
        Assert.Equal("COSINE", findNearest.GetProperty("distanceMeasure").GetString());
        Assert.Equal(limit, findNearest.GetProperty("limit").GetInt32());
        Assert.Equal("vector_distance", findNearest.GetProperty("distanceResultField").GetString());

        var embedding = findNearest.GetProperty("queryVector").GetProperty("mapValue").GetProperty("fields");
        Assert.Equal("__vector__", embedding.GetProperty("__type__").GetProperty("stringValue").GetString());
        
        var values = embedding.GetProperty("value").GetProperty("arrayValue").GetProperty("values");
        Assert.Equal(768, values.GetArrayLength());
        Assert.Equal(0.1, values[0].GetProperty("doubleValue").GetDouble(), 5);
        Assert.Equal(0.9, values[767].GetProperty("doubleValue").GetDouble(), 5);
    }

    [Fact]
    public void ParseRunQueryResponse_ShouldSuccessfullyParseMultipleHits()
    {
        // Arrange
        var rawResponse = @"
[
  {
    ""document"": {
      ""name"": ""projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001/chunks/spike_chunk_002"",
      ""fields"": {
        ""id"": { ""stringValue"": ""spike_chunk_002"" },
        ""userId"": { ""stringValue"": ""spike_test_user_001"" },
        ""documentId"": { ""stringValue"": ""doc_123"" },
        ""documentName"": { ""stringValue"": ""test.pdf"" },
        ""content"": { ""stringValue"": ""赛前48小时建议增加碳水化合物摄入，减少高纤维食物，保持充分水分补充。"" },
        ""chunkIndex"": { ""integerValue"": ""1"" },
        ""pageNumber"": { ""integerValue"": ""2"" },
        ""charStart"": { ""integerValue"": ""200"" },
        ""charEnd"": { ""integerValue"": ""400"" },
        ""vector_distance"": { ""doubleValue"": 0.25 }
      }
    }
  },
  {
    ""document"": {
      ""name"": ""projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001/chunks/spike_chunk_001"",
      ""fields"": {
        ""id"": { ""stringValue"": ""spike_chunk_001"" },
        ""userId"": { ""stringValue"": ""spike_test_user_001"" },
        ""documentId"": { ""stringValue"": ""doc_123"" },
        ""documentName"": { ""stringValue"": ""test.pdf"" },
        ""content"": { ""stringValue"": ""本周训练重点是长距离耐力骑行，目标里程80km，配速保持在25-28km/h区间。"" },
        ""chunkIndex"": { ""integerValue"": ""0"" },
        ""pageNumber"": { ""integerValue"": ""1"" },
        ""charStart"": { ""integerValue"": ""0"" },
        ""charEnd"": { ""integerValue"": ""200"" },
        ""vector_distance"": { ""integerValue"": ""1"" }
      }
    }
  }
]";

        // Act
        var results = RestFirestoreVectorStore.ParseRunQueryResponse(rawResponse);

        // Assert
        Assert.Equal(2, results.Count);

        var first = results[0];
        Assert.Equal("spike_chunk_002", first.Chunk.Id);
        Assert.Equal("spike_test_user_001", first.Chunk.UserId);
        Assert.Equal("doc_123", first.Chunk.DocumentId);
        Assert.Equal("test.pdf", first.Chunk.DocumentName);
        Assert.Equal("赛前48小时建议增加碳水化合物摄入，减少高纤维食物，保持充分水分补充。", first.Chunk.Content);
        Assert.Equal(1, first.Chunk.ChunkIndex);
        Assert.Equal(2, first.Chunk.PageNumber);
        Assert.Equal(200, first.Chunk.CharStart);
        Assert.Equal(400, first.Chunk.CharEnd);
        Assert.Equal(0.25, first.Distance, 5);
        Assert.Equal(0.75, first.Score, 5);

        var second = results[1];
        Assert.Equal("spike_chunk_001", second.Chunk.Id);
        Assert.Equal(1.0, second.Distance, 5);
        Assert.Equal(0.0, second.Score, 5);
    }

    [Fact]
    public void ParseRunQueryResponse_ShouldBeRobustWhenEmptyOrMissingProperties()
    {
        // Arrange & Act
        var resultsFromNull = RestFirestoreVectorStore.ParseRunQueryResponse(null!);
        var resultsFromEmpty = RestFirestoreVectorStore.ParseRunQueryResponse("");
        var resultsFromInvalidJson = RestFirestoreVectorStore.ParseRunQueryResponse("{ invalid }");
        var resultsFromPartialFields = RestFirestoreVectorStore.ParseRunQueryResponse(@"
[
  {
    ""document"": {
      ""name"": ""projects/copper-affinity-467409-k7/databases/(default)/documents/users/spike_test_user_001/chunks/spike_chunk_002"",
      ""fields"": {
        ""content"": { ""stringValue"": ""some content"" }
      }
    }
  }
]");

        // Assert
        Assert.Empty(resultsFromNull);
        Assert.Empty(resultsFromEmpty);
        Assert.Empty(resultsFromInvalidJson);
        Assert.Single(resultsFromPartialFields);
        Assert.Equal("spike_chunk_002", resultsFromPartialFields[0].Chunk.Id);
        Assert.Equal("some content", resultsFromPartialFields[0].Chunk.Content);
        Assert.Equal(-1.0, resultsFromPartialFields[0].Distance);
    }

    [Fact]
    public void ParserAndFiltering_ShouldFilterResultsCorrectlyBasedOnThreshold()
    {
        // Arrange
        var threshold = 0.35;
        var results = new List<VectorSearchResult>
        {
            new VectorSearchResult { Chunk = new KnowledgeChunk { Id = "c1" }, Distance = 0.20 },
            new VectorSearchResult { Chunk = new KnowledgeChunk { Id = "c2" }, Distance = 0.35 },
            new VectorSearchResult { Chunk = new KnowledgeChunk { Id = "c3" }, Distance = 0.36 },
            new VectorSearchResult { Chunk = new KnowledgeChunk { Id = "c4" }, Distance = 0.80 }
        };

        // Act
        var filtered = results.Where(r => r.Distance <= threshold).ToList();

        // Assert
        Assert.Equal(2, filtered.Count);
        Assert.Contains(filtered, r => r.Chunk.Id == "c1");
        Assert.Contains(filtered, r => r.Chunk.Id == "c2");
        Assert.DoesNotContain(filtered, r => r.Chunk.Id == "c3");
        Assert.DoesNotContain(filtered, r => r.Chunk.Id == "c4");
    }
}

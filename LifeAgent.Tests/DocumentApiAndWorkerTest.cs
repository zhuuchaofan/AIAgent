using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Tests;

public class DocumentApiAndWorkerTest
{
    private readonly FileValidator _validator;
    private readonly FakeDocumentRepository _repo;
    private readonly FakeCloudStorageService _storage;
    private readonly FakeCloudTasksService _tasks;
    private readonly IOptions<RagOptions> _ragOptions;
    private readonly FakeDocumentTextExtractor _extractor;
    private readonly IChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly FakeFirestoreVectorStore _vectorStore;
    private readonly DailyQuotaService _unlimitedQuota;

    public DocumentApiAndWorkerTest()
    {
        var options = new RagOptions
        {
            MaxFileSizeMb = 10,
            GcsBucketName = "test-bucket",
            InternalProcessAudience = "https://copper-affinity-467409-k7.appspot.com/"
        };
        _ragOptions = Options.Create(options);
        _validator = new FileValidator(_ragOptions);
        _repo = new FakeDocumentRepository();
        _storage = new FakeCloudStorageService();
        _tasks = new FakeCloudTasksService();
        _extractor = new FakeDocumentTextExtractor();
        _chunker = new BasicChunker();
        _embeddingService = new MockEmbeddingService();
        _vectorStore = new FakeFirestoreVectorStore();
        _unlimitedQuota = new DailyQuotaService(Options.Create(new RagOptions
        {
            DailyLlmCallLimit = 0,
            DailyEmbeddingCallLimit = 0,
            DailyDocumentProcessLimit = 0
        }));
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. Upload API Handlers (POST /api/v1/documents)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UploadDocumentAsync_ShouldSucceedAndSetProcessingStatus()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_123";

        var fileContent = "This is some dummy pdf content.";
        var fileStream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));
        var formFile = new FormFile(fileStream, 0, fileStream.Length, "file", "test.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };

        // Act
        var result = await DocumentEndpoints.UploadDocumentAsync(
            context,
            formFile,
            _validator,
            _storage,
            _repo,
            _tasks,
            NullLogger<RestFirestoreVectorStore>.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(202, statusResult.StatusCode);
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.NotNull(valueResult.Value);

        // 校验 Task 投递
        Assert.True(_tasks.EnqueueCalled);
        Assert.Equal("user_123", _tasks.LastUserId);
        Assert.Equal(_tasks.LastGcsPath, $"gs://test-bucket/users/user_123/documents/{_tasks.LastDocId}/test.pdf");

        // 校验元数据存在于 Firestore Fake 并呈 processing 状态
        var storedDocKey = $"user_123_{_tasks.LastDocId}";
        Assert.True(_repo.Storage.ContainsKey(storedDocKey));
        var storedDoc = _repo.Storage[storedDocKey];
        Assert.Equal("processing", storedDoc.Status);
        Assert.Equal("test.pdf", storedDoc.FileName);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. Cross-Tenant Authorization Checks (GET & DELETE)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDocumentsAsync_ShouldOnlyReturnCurrentUserDocuments()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_123";

        // 向 Fake 仓储注入两个不同用户的数据
        var doc1 = new KnowledgeDocument { Id = "doc_1", UserId = "user_123", FileName = "d1.txt" };
        var doc2 = new KnowledgeDocument { Id = "doc_2", UserId = "user_999", FileName = "d2.txt" };
        await _repo.CreateAsync(doc1);
        await _repo.CreateAsync(doc2);

        // Act
        var result = await DocumentEndpoints.GetDocumentsAsync(context, _repo);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, statusResult.StatusCode);
        var valueResult = Assert.IsAssignableFrom<IValueHttpResult>(result);
        Assert.NotNull(valueResult.Value);
        var val = valueResult.Value;

        // 使用反射直观地取得匿名对象属性，防止多程序集间 JSON 序列化过滤 internal 属性
        var dataProp = val.GetType().GetProperty("data");
        Assert.NotNull(dataProp);
        var dataList = dataProp.GetValue(val) as List<KnowledgeDocument>;
        Assert.NotNull(dataList);
        
        Assert.Single(dataList);
        Assert.Equal("doc_1", dataList[0].Id);
    }

    [Fact]
    public async Task DeleteDocumentAsync_ShouldRejectWhenCrossTenant()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_123"; // 当前登录用户为 123

        // 文档属于 999 用户
        var doc = new KnowledgeDocument { Id = "doc_1", UserId = "user_999", FileName = "d1.txt", GcsPath = "gs://test-bucket/users/user_999/documents/doc_1/d1.txt" };
        await _repo.CreateAsync(doc);

        // Act
        var result = await DocumentEndpoints.DeleteDocumentAsync(context, "doc_1", _repo, _storage, NullLogger<RestFirestoreVectorStore>.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(404, statusResult.StatusCode);
        Assert.False(_storage.DeleteCalled); // GCS 物理文件未被删除
        Assert.True(_repo.Storage.ContainsKey("user_999_doc_1")); // 数据库中元数据保留
    }

    [Fact]
    public async Task DeleteDocumentAsync_ShouldSucceedForOwnedDocument()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_123";

        var doc = new KnowledgeDocument { Id = "doc_1", UserId = "user_123", FileName = "d1.txt", GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt" };
        await _repo.CreateAsync(doc);

        // Act
        var result = await DocumentEndpoints.DeleteDocumentAsync(context, "doc_1", _repo, _storage, NullLogger<RestFirestoreVectorStore>.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, statusResult.StatusCode);
        Assert.True(_storage.DeleteCalled);
        Assert.Equal("gs://test-bucket/users/user_123/documents/doc_1/d1.txt", _storage.LastDeletedPath);
        Assert.False(_repo.Storage.ContainsKey("user_123_doc_1")); // 元数据已彻底抹除
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. Worker Callback Endpoints & Zero-Trust Checks (POST /internal/...)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_ShouldRejectWhenUserMismatch()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        // 文档属于 123
        var doc = new KnowledgeDocument { Id = "doc_1", UserId = "user_123", Status = "processing", GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt" };
        await _repo.CreateAsync(doc);

        // 任务请求越权地指明是 999 用户的任务
        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_1",
            UserId = "user_999",
            GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt"
        };

        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context,
            request,
            _repo,
            _storage,
            _extractor,
            _chunker,
            _embeddingService,
            _vectorStore,
            fakeEnv,
            _ragOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(403, statusResult.StatusCode); // 越权校验被拦截
    }

    [Fact]
    public async Task ProcessDocumentAsync_ShouldRejectWhenGcsPathBypassed()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument { Id = "doc_1", UserId = "user_123", Status = "processing", GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt" };
        await _repo.CreateAsync(doc);

        // 企图访问不属于 doc_1 的 GCS 目录 (隔离攻击)
        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_1",
            UserId = "user_123",
            GcsPath = "gs://test-bucket/users/user_123/documents/doc_999/d1.txt" // 非法路径
        };

        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context,
            request,
            _repo,
            _storage,
            _extractor,
            _chunker,
            _embeddingService,
            _vectorStore,
            fakeEnv,
            _ragOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(403, statusResult.StatusCode);
    }

    [Fact]
    public async Task ProcessDocumentAsync_ShouldRejectWhenStatusNotProcessing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        // 已经是 failed 状态，应当被拒绝
        var doc = new KnowledgeDocument { Id = "doc_1", UserId = "user_123", Status = "failed", GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt" };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_1",
            UserId = "user_123",
            GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt"
        };

        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context,
            request,
            _repo,
            _storage,
            _extractor,
            _chunker,
            _embeddingService,
            _vectorStore,
            fakeEnv,
            _ragOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, statusResult.StatusCode);
    }

    [Fact]
    public async Task ProcessDocumentAsync_ShouldSucceedAndMarkSuccessOnValidPayload()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument { Id = "doc_1", UserId = "user_123", Status = "processing", GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt" };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_1",
            UserId = "user_123",
            GcsPath = "gs://test-bucket/users/user_123/documents/doc_1/d1.txt"
        };

        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context,
            request,
            _repo,
            _storage,
            _extractor,
            _chunker,
            _embeddingService,
            _vectorStore,
            fakeEnv,
            _ragOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, statusResult.StatusCode);
        
        var updatedDoc = _repo.Storage["user_123_doc_1"];
        Assert.Equal("success", updatedDoc.Status);
        Assert.True(updatedDoc.ChunkCount > 0);
    }
}

// ────────────────────────────────────────────────────────────────────────
// Test Double / Mock 辅助实现类
// ────────────────────────────────────────────────────────────────────────

public class FakeDocumentRepository : IDocumentRepository
{
    public Dictionary<string, KnowledgeDocument> Storage { get; } = new();

    public Task CreateAsync(KnowledgeDocument doc)
    {
        Storage[$"{doc.UserId}_{doc.Id}"] = doc;
        return Task.CompletedTask;
    }

    public Task<KnowledgeDocument?> GetAsync(string userId, string documentId)
    {
        Storage.TryGetValue($"{userId}_{documentId}", out var doc);
        return Task.FromResult(doc);
    }

    public Task<List<KnowledgeDocument>> ListAsync(string userId, int limit, string? cursor)
    {
        var list = Storage.Values.Where(d => d.UserId == userId).Take(limit).ToList();
        return Task.FromResult(list);
    }

    public Task UpdateAsync(KnowledgeDocument doc)
    {
        Storage[$"{doc.UserId}_{doc.Id}"] = doc;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string userId, string documentId)
    {
        Storage.Remove($"{userId}_{documentId}");
        return Task.CompletedTask;
    }
}

public class FakeCloudStorageService : ICloudStorageService
{
    public bool UploadCalled { get; set; }
    public bool DeleteCalled { get; set; }
    public string LastDeletedPath { get; set; } = "";

    public Task<string> UploadFileAsync(string userId, string documentId, string fileName, Stream fileStream, string mimeType)
    {
        UploadCalled = true;
        return Task.FromResult($"gs://test-bucket/users/{userId}/documents/{documentId}/{Uri.EscapeDataString(fileName)}");
    }

    public Task<Stream> DownloadFileAsync(string userId, string documentId, string fileName)
    {
        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("file content")));
    }

    public Task<Stream> DownloadFileByPathAsync(string gcsPath)
    {
        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("file content")));
    }

    public Task DeleteFileByPathAsync(string gcsPath)
    {
        DeleteCalled = true;
        LastDeletedPath = gcsPath;
        return Task.CompletedTask;
    }
}

public class FakeCloudTasksService : ICloudTasksService
{
    public bool EnqueueCalled { get; set; }
    public string LastUserId { get; set; } = "";
    public string LastDocId { get; set; } = "";
    public string LastGcsPath { get; set; } = "";

    public Task EnqueueIngestTaskAsync(string userId, string documentId, string gcsPath)
    {
        EnqueueCalled = true;
        LastUserId = userId;
        LastDocId = documentId;
        LastGcsPath = gcsPath;
        return Task.CompletedTask;
    }
}

public class FakeWebHostEnvironment : IWebHostEnvironment
{
    public string EnvironmentName { get; set; } = "Production";
    public string ApplicationName { get; set; } = "LifeAgent.Api";
    public string WebRootPath { get; set; } = "";
    public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
    public string ContentRootPath { get; set; } = "";
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
}

public class FakeDocumentTextExtractor : IDocumentTextExtractor
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public Task<ExtractionResult> ExtractTextAsync(Stream stream, string mimeType)
    {
        var res = new ExtractionResult();
        if (Success)
        {
            res.Success = true;
            res.RawText = "extracted text content";
            res.Pages.Add(new PageTextInfo { PageNumber = 1, Text = "extracted text content", CharStart = 0, CharEnd = 22 });
        }
        else
        {
            res.Success = false;
            res.ErrorMessage = ErrorMessage ?? "Extractor failure.";
        }
        return Task.FromResult(res);
    }
}

public class FakeFirestoreVectorStore : IFirestoreVectorStore
{
    public bool DeleteCalled { get; set; }
    public string? LastDeletedDocId { get; set; }
    public bool WriteCalled { get; set; }
    public List<KnowledgeChunk>? LastWrittenChunks { get; set; }

    public Task WriteChunksAsync(string userId, List<KnowledgeChunk> chunks, List<float[]> embeddings)
    {
        WriteCalled = true;
        LastWrittenChunks = chunks;
        return Task.CompletedTask;
    }

    public Task<List<VectorSearchResult>> FindNearestAsync(string userId, float[] queryVector, int limit)
    {
        return Task.FromResult(new List<VectorSearchResult>());
    }

    public Task DeleteChunksByDocumentIdAsync(string userId, string documentId)
    {
        DeleteCalled = true;
        LastDeletedDocId = documentId;
        return Task.CompletedTask;
    }
}

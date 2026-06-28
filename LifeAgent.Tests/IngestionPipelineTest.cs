using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Tests;

public class IngestionPipelineTest
{
    private readonly FakeDocumentRepository _repo;
    private readonly FakeCloudStorageService _storage;
    private readonly FakeDocumentTextExtractor _extractor;
    private readonly IChunker _chunker;
    private readonly IEmbeddingService _embeddingService;
    private readonly FakeFirestoreVectorStore _vectorStore;
    private readonly IOptions<RagOptions> _ragOptions;
    private readonly DailyQuotaService _unlimitedQuota;

    public IngestionPipelineTest()
    {
        _repo = new FakeDocumentRepository();
        _storage = new FakeCloudStorageService();
        _extractor = new FakeDocumentTextExtractor();
        _chunker = new BasicChunker();
        _embeddingService = new MockEmbeddingService();
        _vectorStore = new FakeFirestoreVectorStore();

        var options = new RagOptions
        {
            MaxFileSizeMb = 10,
            GcsBucketName = "test-bucket",
            InternalProcessAudience = "https://copper-affinity-467409-k7.appspot.com/"
        };
        _ragOptions = Options.Create(options);
        _unlimitedQuota = new DailyQuotaService(Options.Create(new RagOptions
        {
            DailyLlmCallLimit = 0,
            DailyEmbeddingCallLimit = 0,
            DailyDocumentProcessLimit = 0
        }));
    }

    [Fact]
    public async Task ProcessDocumentAsync_SuccessPath_UpdatesMetadataAndWritesVectors()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_txt_1",
            UserId = "user_abc",
            Status = "processing",
            FileName = "test.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_txt_1/test.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_txt_1",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_txt_1/test.txt"
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
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        // check document status
        var updated = await _repo.GetAsync("user_abc", "doc_txt_1");
        Assert.NotNull(updated);
        Assert.Equal("success", updated.Status);
        Assert.True(updated.ChunkCount > 0);
        Assert.Null(updated.ErrorMessage);

        // check chunk write
        Assert.True(_vectorStore.WriteCalled);
        Assert.NotNull(_vectorStore.LastWrittenChunks);
        Assert.Single(_vectorStore.LastWrittenChunks);

        var writtenChunk = _vectorStore.LastWrittenChunks[0];
        Assert.Equal("doc_txt_1", writtenChunk.DocumentId);
        Assert.Equal(0, writtenChunk.ChunkIndex);
        Assert.Equal("extracted text content", writtenChunk.Content);
    }

    [Fact]
    public async Task MockEmbeddingService_ShouldReturnNormalized768Dimensions()
    {
        // Arrange
        var service = new MockEmbeddingService();
        var text = "hello world";

        // Act
        var vec = await service.GenerateEmbeddingAsync(text);

        // Assert
        Assert.NotNull(vec);
        Assert.Equal(768, vec.Length);

        // Verify normalized (magnitude close to 1.0)
        double sumSquare = 0;
        foreach (var v in vec)
        {
            sumSquare += v * v;
        }
        Assert.True(Math.Abs(sumSquare - 1.0) < 1e-5);
    }

    [Fact]
    public async Task ProcessDocumentAsync_BadEmbeddingDimensions_FailsAndCleans()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_bad_emb",
            UserId = "user_abc",
            Status = "processing",
            FileName = "test.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_bad_emb/test.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_bad_emb",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_bad_emb/test.txt"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        var badEmb = new BadEmbeddingService { Dimensions = 512 }; // Expected 768

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context,
            request,
            _repo,
            _storage,
            _extractor,
            _chunker,
            badEmb,
            _vectorStore,
            fakeEnv,
            _ragOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var errResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(500, errResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_bad_emb");
        Assert.NotNull(updated);
        Assert.Equal("failed", updated.Status);
        Assert.Contains("Expected 768 dimensions embedding", updated.ErrorMessage);

        // cascading cleanup should have been triggered
        Assert.True(_vectorStore.DeleteCalled);
        Assert.Equal("doc_bad_emb", _vectorStore.LastDeletedDocId);
    }

    [Fact]
    public async Task ProcessDocumentAsync_TextExtractorFailed_FailsAndCleans()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_fail_ext",
            UserId = "user_abc",
            Status = "processing",
            FileName = "test.pdf",
            MimeType = "application/pdf",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_fail_ext/test.pdf"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_fail_ext",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_fail_ext/test.pdf"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        _extractor.Success = false;
        _extractor.ErrorMessage = "Failed to parse PDF: The file format is invalid, encrypted or corrupted.";

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
        var errResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(500, errResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_fail_ext");
        Assert.NotNull(updated);
        Assert.Equal("failed", updated.Status);
        Assert.Equal("Failed to parse PDF: The file format is invalid, encrypted or corrupted.", updated.ErrorMessage);

        Assert.True(_vectorStore.DeleteCalled);
        Assert.Equal("doc_fail_ext", _vectorStore.LastDeletedDocId);
    }

    [Fact]
    public async Task ProcessDocumentAsync_ChunkerGeneratesZeroChunks_FailsAndCleans()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_zero_chunk",
            UserId = "user_abc",
            Status = "processing",
            FileName = "test.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_zero_chunk/test.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_zero_chunk",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_zero_chunk/test.txt"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        var zeroChunker = new ZeroChunker();

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context,
            request,
            _repo,
            _storage,
            _extractor,
            zeroChunker,
            _embeddingService,
            _vectorStore,
            fakeEnv,
            _ragOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var errResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(500, errResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_zero_chunk");
        Assert.NotNull(updated);
        Assert.Equal("failed", updated.Status);
        Assert.Contains("No valid content or chunks could be generated", updated.ErrorMessage);

        Assert.True(_vectorStore.DeleteCalled);
        Assert.Equal("doc_zero_chunk", _vectorStore.LastDeletedDocId);
    }

    [Fact]
    public async Task ProcessDocumentAsync_IdempotencySuccess_SkippedWith200()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_already_success",
            UserId = "user_abc",
            Status = "success",
            FileName = "test.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_already_success/test.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_already_success",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_already_success/test.txt"
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
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        Assert.False(_vectorStore.WriteCalled);
    }

    [Fact]
    public async Task ProcessDocumentAsync_IdempotencyFailed_RejectedWith400()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_already_failed",
            UserId = "user_abc",
            Status = "failed",
            FileName = "test.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_already_failed/test.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_already_failed",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_already_failed/test.txt"
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
        var badResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(400, badResult.StatusCode);
    }

    [Fact]
    public async Task ProcessDocumentAsync_RetriedTask_CleansLegacyChunksBeforeWrite()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_retry_clean",
            UserId = "user_abc",
            Status = "processing",
            FileName = "test.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_retry_clean/test.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_retry_clean",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_retry_clean/test.txt"
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
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        Assert.True(_vectorStore.DeleteCalled);
        Assert.Equal("doc_retry_clean", _vectorStore.LastDeletedDocId);
        Assert.True(_vectorStore.WriteCalled);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Phase 3.5: Chunk 数量限制集成测试
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessDocumentAsync_BelowChunkLimit_IsTruncatedFalse()
    {
        // Arrange: FixedCountChunker 产生 3 个 chunk，MaxChunksPerDocument = 5 → 不截断
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_below_limit",
            UserId = "user_abc",
            Status = "processing",
            FileName = "small.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_below_limit/small.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_below_limit",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_below_limit/small.txt"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        var fixedChunker = new FixedCountChunker(desiredCount: 3);
        var options = new RagOptions
        {
            MaxFileSizeMb = 10,
            MaxChunksPerDocument = 5,
            GcsBucketName = "test-bucket",
            InternalProcessAudience = "https://copper-affinity-467409-k7.appspot.com/"
        };
        var limitedOptions = Options.Create(options);

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context, request, _repo, _storage, _extractor, fixedChunker,
            _embeddingService, _vectorStore, fakeEnv, limitedOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_below_limit");
        Assert.NotNull(updated);
        Assert.Equal("success", updated.Status);
        Assert.Equal(3, updated.ChunkCount);
        Assert.False(updated.IsTruncated);
    }

    [Fact]
    public async Task ProcessDocumentAsync_AtChunkLimit_IsTruncatedFalse()
    {
        // Arrange: FixedCountChunker 产生恰好 5 个 chunk，MaxChunksPerDocument = 5 → 不截断
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_at_limit",
            UserId = "user_abc",
            Status = "processing",
            FileName = "exact.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_at_limit/exact.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_at_limit",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_at_limit/exact.txt"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // FixedCountChunker: 当 maxChunks >= desiredCount 时返回 desiredCount 个 chunk
        // 传入 maxChunks+1=6 >= 5 → 返回 5 个 chunk → 5 == maxChunks → 不截断
        var fixedChunker = new FixedCountChunker(desiredCount: 5);
        var options = new RagOptions
        {
            MaxFileSizeMb = 10,
            MaxChunksPerDocument = 5,
            GcsBucketName = "test-bucket",
            InternalProcessAudience = "https://copper-affinity-467409-k7.appspot.com/"
        };
        var limitedOptions = Options.Create(options);

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context, request, _repo, _storage, _extractor, fixedChunker,
            _embeddingService, _vectorStore, fakeEnv, limitedOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_at_limit");
        Assert.NotNull(updated);
        Assert.Equal("success", updated.Status);
        Assert.Equal(5, updated.ChunkCount);
        Assert.False(updated.IsTruncated);

        // 全部 5 个 chunk 应被写入
        Assert.True(_vectorStore.WriteCalled);
        Assert.NotNull(_vectorStore.LastWrittenChunks);
        Assert.Equal(5, _vectorStore.LastWrittenChunks.Count);
    }

    [Fact]
    public async Task ProcessDocumentAsync_ExceedsChunkLimit_IsTruncatedTrueAndChunksLimited()
    {
        // Arrange: FixedCountChunker 产生 8 个 chunk，MaxChunksPerDocument = 5 → 截断到 5
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_truncated",
            UserId = "user_abc",
            Status = "processing",
            FileName = "large.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_truncated/large.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_truncated",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_truncated/large.txt"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // FixedCountChunker(desiredCount: 8):
        //   传入 maxChunks+1=6 → 8 > 6 → chunker 截断到 6 个返回
        //   worker: 6 > maxChunks(5) → isTruncated = true → Take(5)
        var fixedChunker = new FixedCountChunker(desiredCount: 8);
        var options = new RagOptions
        {
            MaxFileSizeMb = 10,
            MaxChunksPerDocument = 5,
            GcsBucketName = "test-bucket",
            InternalProcessAudience = "https://copper-affinity-467409-k7.appspot.com/"
        };
        var limitedOptions = Options.Create(options);

        // Act
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context, request, _repo, _storage, _extractor, fixedChunker,
            _embeddingService, _vectorStore, fakeEnv, limitedOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_truncated");
        Assert.NotNull(updated);
        Assert.Equal("success", updated.Status);
        Assert.Equal(5, updated.ChunkCount);
        Assert.True(updated.IsTruncated);

        // 只写入 5 个 chunk（而非 8 个）
        Assert.True(_vectorStore.WriteCalled);
        Assert.NotNull(_vectorStore.LastWrittenChunks);
        Assert.Equal(5, _vectorStore.LastWrittenChunks.Count);
    }

    [Fact]
    public async Task ProcessDocumentAsync_MissingMaxChunksConfig_DefaultsTo200()
    {
        // Arrange: MaxChunksPerDocument = 0（未配置），应默认使用 200
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer dev-token";

        var doc = new KnowledgeDocument
        {
            Id = "doc_default_limit",
            UserId = "user_abc",
            Status = "processing",
            FileName = "default.txt",
            MimeType = "text/plain",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_default_limit/default.txt"
        };
        await _repo.CreateAsync(doc);

        var request = new ProcessDocumentRequest
        {
            DocumentId = "doc_default_limit",
            UserId = "user_abc",
            GcsPath = "gs://test-bucket/users/user_abc/documents/doc_default_limit/default.txt"
        };
        var fakeEnv = new FakeWebHostEnvironment { EnvironmentName = "Development" };

        // MaxChunksPerDocument = 0 → 应 fallback 到 200
        var options = new RagOptions
        {
            MaxFileSizeMb = 10,
            MaxChunksPerDocument = 0,
            GcsBucketName = "test-bucket",
            InternalProcessAudience = "https://copper-affinity-467409-k7.appspot.com/"
        };
        var defaultOptions = Options.Create(options);

        // Act: 使用默认 extractor（只产生 1 个 chunk），不应截断
        var result = await InternalDocumentEndpoints.ProcessDocumentAsync(
            context, request, _repo, _storage, _extractor, _chunker,
            _embeddingService, _vectorStore, fakeEnv, defaultOptions,
            _unlimitedQuota,
            NullLoggerFactory.Instance);

        // Assert
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var updated = await _repo.GetAsync("user_abc", "doc_default_limit");
        Assert.NotNull(updated);
        Assert.Equal("success", updated.Status);
        Assert.Equal(1, updated.ChunkCount);
        Assert.False(updated.IsTruncated);
    }
}

public class BadEmbeddingService : IEmbeddingService
{
    public int Dimensions { get; set; } = 128;
    public Task<float[]> GenerateEmbeddingAsync(string text)
    {
        return Task.FromResult(new float[Dimensions]);
    }
}

public class ZeroChunker : IChunker
{
    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text)
    {
        return new List<KnowledgeChunk>();
    }
    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages)
    {
        return new List<KnowledgeChunk>();
    }
    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text, int maxChunks)
    {
        return new List<KnowledgeChunk>();
    }
    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages, int maxChunks)
    {
        return new List<KnowledgeChunk>();
    }
}

/// <summary>
/// 产生大量文本的 Fake Extractor，用于测试 chunk 截断。
/// 生成 ~30 个段落，预计产生 5+ 个 chunk（TargetSize=800 字符/chunk）。
/// </summary>
public class LargeTextFakeExtractor : IDocumentTextExtractor
{
    public Task<ExtractionResult> ExtractTextAsync(Stream stream, string mimeType)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i <= 30; i++)
        {
            sb.AppendLine($"这是第{i}个自然段落，用于产生足够多的文本内容以触发多次分块。每个段落大约一百个字符左右，确保文本总量足够大，以便分块器产生足够多的 chunks 用于测试截断行为。");
        }
        var text = sb.ToString();

        var res = new ExtractionResult
        {
            Success = true,
            RawText = text
        };
        res.Pages.Add(new PageTextInfo
        {
            PageNumber = 1,
            Text = text,
            CharStart = 0,
            CharEnd = text.Length
        });
        return Task.FromResult(res);
    }
}

/// <summary>
/// 产生固定数量 chunk 的 Fake Chunker，用于精确测试截断边界。
/// desiredCount: 期望产生的 chunk 总数（如果 maxChunks >= desiredCount 则返回 desiredCount 个，否则截断到 maxChunks）。
/// </summary>
public class FixedCountChunker : IChunker
{
    private readonly int _desiredCount;

    public FixedCountChunker(int desiredCount)
    {
        _desiredCount = desiredCount;
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text)
    {
        return GenerateChunks(userId, documentId, documentName, _desiredCount);
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages)
    {
        return GenerateChunks(userId, documentId, documentName, _desiredCount);
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text, int maxChunks)
    {
        var count = Math.Min(_desiredCount, maxChunks);
        return GenerateChunks(userId, documentId, documentName, count);
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages, int maxChunks)
    {
        var count = Math.Min(_desiredCount, maxChunks);
        return GenerateChunks(userId, documentId, documentName, count);
    }

    private static List<KnowledgeChunk> GenerateChunks(string userId, string documentId, string documentName, int count)
    {
        var chunks = new List<KnowledgeChunk>();
        for (int i = 0; i < count; i++)
        {
            chunks.Add(new KnowledgeChunk
            {
                Id = $"{documentId}_{i}",
                UserId = userId,
                DocumentId = documentId,
                DocumentName = documentName,
                ChunkIndex = i,
                PageNumber = 1,
                CharStart = i * 800,
                CharEnd = (i + 1) * 800,
                Content = $"Chunk {i} content for testing.",
                CreatedAt = DateTime.UtcNow
            });
        }
        return chunks;
    }
}

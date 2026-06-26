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
            NullLoggerFactory.Instance);

        // Assert
        var okResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        Assert.True(_vectorStore.DeleteCalled);
        Assert.Equal("doc_retry_clean", _vectorStore.LastDeletedDocId);
        Assert.True(_vectorStore.WriteCalled);
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
}

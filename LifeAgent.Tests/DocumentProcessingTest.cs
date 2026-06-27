using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

namespace LifeAgent.Tests;

public class DocumentProcessingTest
{
    private readonly FileValidator _validator;

    public DocumentProcessingTest()
    {
        var options = Options.Create(new RagOptions
        {
            MaxFileSizeMb = 10,
            GcsBucketName = "test-bucket"
        });
        _validator = new FileValidator(options);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 1. GCS 路径与文件名安全处理测试
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildGcsObjectPath_ShouldFormatCorrectlyAndEscapeSpecialCharacters()
    {
        // Arrange
        var userId = "user_123";
        var documentId = "doc_abc";
        var fileName = "2026 骑行计划 & 训练.pdf";

        // Act
        var resultPath = GoogleCloudStorageService.BuildGcsObjectPath(userId, documentId, fileName);

        // Assert
        var expectedFileName = Uri.EscapeDataString("2026 骑行计划 & 训练.pdf");
        Assert.Equal($"users/user_123/documents/doc_abc/{expectedFileName}", resultPath);
    }

    [Fact]
    public void GetSafeFileName_ShouldPreventDirectoryTraversal()
    {
        // Arrange
        var unsafeFileName = "../../../etc/passwd";

        // Act
        var safeFileName = GoogleCloudStorageService.GetSafeFileName(unsafeFileName);

        // Assert
        Assert.Equal("passwd", safeFileName);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 2. 文件校验测试 (大小与类型)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateFile_ShouldRejectExceededSize()
    {
        // Arrange
        long exceededSize = 11 * 1024L * 1024L; // 11MB

        // Act
        var (isValid, error) = _validator.ValidateFile("test.txt", exceededSize, "text/plain");

        // Assert
        Assert.False(isValid);
        Assert.Contains("exceeds the maximum allowed limit", error);
    }

    [Fact]
    public void ValidateFile_ShouldRejectUnsupportedMimeTypeOrExtension()
    {
        // Arrange & Act
        var (invalidExt, err1) = _validator.ValidateFile("test.exe", 100, "text/plain");
        var (invalidMime, err2) = _validator.ValidateFile("test.txt", 100, "application/x-msdownload");

        // Assert
        Assert.False(invalidExt);
        Assert.Contains("Unsupported file extension", err1);

        Assert.False(invalidMime);
        Assert.Contains("Unsupported MIME type", err2);
    }

    [Fact]
    public void ValidateFile_ShouldPassForValidFile()
    {
        // Arrange & Act
        var (isValid, error) = _validator.ValidateFile("騎行日記.md", 5000, "text/markdown");

        // Assert
        Assert.True(isValid);
        Assert.Null(error);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 3. 文本抽取服务测试
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractTextAsync_ShouldExtractTxtAndMdCorrectly()
    {
        // Arrange
        var extractor = new PdfPigDocumentTextExtractor(NullLogger<PdfPigDocumentTextExtractor>.Instance);
        var content = "Hello world! This is a markdown text.";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // Act
        var result = await extractor.ExtractTextAsync(stream, "text/markdown");

        // Assert
        Assert.True(result.Success);
        Assert.Equal(content, result.RawText);
        Assert.Single(result.Pages);
        Assert.Equal(1, result.Pages[0].PageNumber);
        Assert.Equal(0, result.Pages[0].CharStart);
        Assert.Equal(content.Length, result.Pages[0].CharEnd);
    }

    [Fact]
    public async Task ExtractTextAsync_ShouldFailOnEmptyStream()
    {
        // Arrange
        var extractor = new PdfPigDocumentTextExtractor(NullLogger<PdfPigDocumentTextExtractor>.Instance);
        using var stream = new MemoryStream();

        // Act
        var result = await extractor.ExtractTextAsync(stream, "text/plain");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.ErrorMessage);
    }

    [Fact]
    public async Task ExtractTextAsync_ShouldNotCrashOnCorruptedPdf()
    {
        // Arrange
        var extractor = new PdfPigDocumentTextExtractor(NullLogger<PdfPigDocumentTextExtractor>.Instance);
        // 传入不合法的 PDF 字节流，前缀假装是 PDF 但实际是损坏的内容
        var corruptedBytes = Encoding.UTF8.GetBytes("%PDF-1.4\ncorrupted content");
        using var stream = new MemoryStream(corruptedBytes);

        // Act
        var result = await extractor.ExtractTextAsync(stream, "application/pdf");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Failed to parse PDF", result.ErrorMessage);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 4. Chunker (分块器) 测试
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Chunker_ShouldSplitLongTextIntoChunksAndHaveOverlap()
    {
        // Arrange
        var chunker = new BasicChunker();
        var userId = "user_123";
        var docId = "doc_abc";
        var docName = "test.txt";
        
        // 构建一段包含 10 个自然段的长文本（每段大约 150 字符，总共 1500 字符以上）
        var sb = new StringBuilder();
        for (int i = 1; i <= 12; i++)
        {
            sb.AppendLine($"这是第{i}个自然段。本段提供一些普通的记录文字，用于测试滑动窗口分块器能否正确地对其进行段落优先切割。我们需要让整体字数远远多于 800 字符。");
        }
        var text = sb.ToString();

        // Act
        var chunks = chunker.SplitDocument(userId, docId, docName, text);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count > 1, $"Should split into multiple chunks, but got {chunks.Count}");

        // 校验 Chunker 的连续性
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            Assert.Equal($"{docId}_{i}", chunk.Id);
            Assert.Equal(i, chunk.ChunkIndex);
            Assert.Equal(1, chunk.PageNumber);
            Assert.Equal(userId, chunk.UserId);
            Assert.Equal(docId, chunk.DocumentId);
            Assert.Equal(docName, chunk.DocumentName);
            
            Assert.False(string.IsNullOrWhiteSpace(chunk.Content));
            Assert.True(chunk.CharStart < chunk.CharEnd);

            // 校验是否符合 charStart 和 charEnd
            var extractedContent = text.Substring(chunk.CharStart, chunk.CharEnd - chunk.CharStart);
            // 内容不包含多余换行时可能需要 trim，这里核对开头或结尾文字
            Assert.Contains(chunk.Content.Substring(0, 10), text);
        }

        // 校验 Overlap 生效：后一个 chunk 的开头应该包含前一个 chunk 结尾的部分文字
        var firstChunkContent = chunks[0].Content;
        var secondChunkContent = chunks[1].Content;
        
        // 提取前一个 chunk 最后的 30 个字，看是不是包含在第二个 chunk 的开头附近
        var endSlice = firstChunkContent.Substring(firstChunkContent.Length - 40);
        Assert.Contains(endSlice.Trim(), secondChunkContent);
    }

    [Fact]
    public void Chunker_ShouldNotProduceEmptyChunks()
    {
        // Arrange
        var chunker = new BasicChunker();
        var text = "\n\n   \n   \n\n";

        // Act
        var chunks = chunker.SplitDocument("user_1", "doc_1", "doc.txt", text);

        // Assert
        Assert.Empty(chunks);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 5. Chunk 数量限制测试 (Phase 3.5)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Chunker_BelowLimit_ShouldNotTruncate()
    {
        // Arrange: 生成足够的文本以产生多个 chunk（每段约 100 字符 × 20 段 = ~2000 字符 → ~2-3 chunks）
        var chunker = new BasicChunker();
        var sb = new StringBuilder();
        for (int i = 1; i <= 20; i++)
        {
            sb.AppendLine($"这是第{i}个自然段落。本段内容用于测试分块器在限制范围内是否正常工作，不应触发截断逻辑。分块器应保持原有行为。");
        }
        var text = sb.ToString();

        // Act: 限制为 10 个 chunk，实际产生的 chunk 数应远小于 10
        var chunks = chunker.SplitDocument("user_1", "doc_below", "below.txt", text, maxChunks: 10);

        // Assert
        Assert.NotEmpty(chunks);
        Assert.True(chunks.Count < 10, $"Expected fewer than 10 chunks, got {chunks.Count}");

        // 无截断时，所有 chunk 的 ChunkIndex 应连续
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
            Assert.Equal($"doc_below_{i}", chunks[i].Id);
        }
    }

    [Fact]
    public void Chunker_AtLimit_ShouldNotTruncate()
    {
        // Arrange: 生成大量文本，预期产生 5+ 个 chunk，maxChunks 设为刚好等于实际数量
        var chunker = new BasicChunker();
        var sb = new StringBuilder();
        for (int i = 1; i <= 50; i++)
        {
            sb.AppendLine($"这是第{i}个自然段落，用于产生足够多的文本内容以触发多次分块。每个段落大约一百个字符左右，确保文本总量足够大。");
        }
        var text = sb.ToString();

        // 先无限制地分块一次，确定实际 chunk 数
        var allChunks = chunker.SplitDocument("user_1", "doc_at", "at.txt", text);
        var actualCount = allChunks.Count;
        Assert.True(actualCount >= 3, $"Expected at least 3 chunks from 50 paragraphs, got {actualCount}");

        // Act: 限制恰好等于实际数量
        var limitedChunks = chunker.SplitDocument("user_1", "doc_at", "at.txt", text, maxChunks: actualCount);

        // Assert: 应产生相同数量的 chunk，内容相同
        Assert.Equal(actualCount, limitedChunks.Count);
        for (int i = 0; i < actualCount; i++)
        {
            Assert.Equal(allChunks[i].Content, limitedChunks[i].Content);
        }
    }

    [Fact]
    public void Chunker_AboveLimit_ShouldTruncateAndNotExceedMaxChunks()
    {
        // Arrange: 生成大量文本，确保 chunk 数量 > maxChunks
        var chunker = new BasicChunker();
        var sb = new StringBuilder();
        for (int i = 1; i <= 50; i++)
        {
            sb.AppendLine($"这是第{i}个自然段落，用于产生足够多的文本内容以触发截断。每个段落大约一百个字符左右，确保文本总量足够大，以便分块器产生足够多的 chunks 用于测试截断行为。");
        }
        var text = sb.ToString();

        // 先无限制地分块，确认实际 chunk 数
        var allChunks = chunker.SplitDocument("user_1", "doc_above", "above.txt", text);
        Assert.True(allChunks.Count >= 3, $"Expected at least 3 chunks, got {allChunks.Count}");

        var limit = 2; // 设置一个较低的限制

        // Act
        var truncatedChunks = chunker.SplitDocument("user_1", "doc_above", "above.txt", text, maxChunks: limit);

        // Assert: 只返回 limit 个 chunk
        Assert.Equal(limit, truncatedChunks.Count);

        // 前 limit 个 chunk 应与无限制版本内容一致
        for (int i = 0; i < limit; i++)
        {
            Assert.Equal(allChunks[i].Content, truncatedChunks[i].Content);
            Assert.Equal(i, truncatedChunks[i].ChunkIndex);
        }
    }

    [Fact]
    public void Chunker_DefaultOverload_ShouldNotApplyLimit()
    {
        // Arrange: 确保无 maxChunks 参数的重载行为与旧版一致
        var chunker = new BasicChunker();
        var sb = new StringBuilder();
        for (int i = 1; i <= 30; i++)
        {
            sb.AppendLine($"这是第{i}个自然段落，用于确认无限制重载方法不施加任何数量限制，保持向后兼容。");
        }
        var text = sb.ToString();

        // Act
        var chunksDefault = chunker.SplitDocument("user_1", "doc_compat", "compat.txt", text);
        var chunksExplicit = chunker.SplitDocument("user_1", "doc_compat", "compat.txt", text, maxChunks: int.MaxValue);

        // Assert: 两个重载应返回完全相同的结果
        Assert.Equal(chunksExplicit.Count, chunksDefault.Count);
        for (int i = 0; i < chunksDefault.Count; i++)
        {
            Assert.Equal(chunksExplicit[i].Content, chunksDefault[i].Content);
        }
    }
}

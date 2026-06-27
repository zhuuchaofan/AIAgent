using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IChunker
{
    List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text);
    List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages);

    // Phase 3.5: 带最大 chunk 数量限制的重载，截断后返回的列表长度 ≤ maxChunks
    List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text, int maxChunks);
    List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages, int maxChunks);
}

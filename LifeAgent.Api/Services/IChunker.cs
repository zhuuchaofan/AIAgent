using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IChunker
{
    List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text);
    List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages);
}

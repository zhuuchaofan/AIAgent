namespace LifeAgent.Api.Models;

public class VectorSearchResult
{
    public KnowledgeChunk Chunk { get; set; } = null!;
    public double Distance { get; set; } // 余弦距离
    public double Score => 1.0 - Distance; // 相似度得分 (1.0 - Cosine Distance)
}

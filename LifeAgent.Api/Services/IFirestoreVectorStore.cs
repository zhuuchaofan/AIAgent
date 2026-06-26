using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IFirestoreVectorStore
{
    Task WriteChunksAsync(string userId, List<KnowledgeChunk> chunks, List<float[]> embeddings);
    Task<List<VectorSearchResult>> FindNearestAsync(string userId, float[] queryVector, int limit);
}

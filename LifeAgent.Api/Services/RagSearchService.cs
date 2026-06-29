using LifeAgent.Api.Models;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services;

public class RagSearchService : IRagSearchService
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IFirestoreVectorStore _vectorStore;
    private readonly IDocumentRepository _documentRepository;
    private readonly RagOptions _ragOptions;

    public RagSearchService(
        IEmbeddingService embeddingService,
        IFirestoreVectorStore vectorStore,
        IDocumentRepository documentRepository,
        IOptions<RagOptions> ragOptions)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _documentRepository = documentRepository;
        _ragOptions = ragOptions.Value;
    }

    public async Task<List<VectorSearchResult>> SearchAsync(
        string userId,
        string query,
        IReadOnlyList<string>? documentIds,
        int? topK,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be null or empty.", nameof(query));
        }

        var allowedDocumentIds = await ValidateDocumentIdsAsync(userId, documentIds);
        var queryVector = await _embeddingService.GenerateEmbeddingAsync(query);
        if (queryVector == null || queryVector.Length != 768)
        {
            throw new InvalidOperationException($"Query embedding generation failed or dimension is not 768. Actual dimension: {queryVector?.Length ?? 0}");
        }

        var limit = Math.Clamp(topK ?? _ragOptions.TopK, 1, Math.Max(_ragOptions.TopK, 10));
        var searchResults = await _vectorStore.FindNearestAsync(userId, queryVector, limit);
        var filtered = new List<VectorSearchResult>();

        foreach (var result in searchResults)
        {
            if (allowedDocumentIds is { Count: > 0 } &&
                !allowedDocumentIds.Contains(result.Chunk.DocumentId))
            {
                continue;
            }

            if (result.Distance <= _ragOptions.DistanceThreshold)
            {
                filtered.Add(result);
            }
        }

        return filtered;
    }

    private async Task<HashSet<string>?> ValidateDocumentIdsAsync(string userId, IReadOnlyList<string>? documentIds)
    {
        if (documentIds == null || documentIds.Count == 0)
        {
            return null;
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var documentId in documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var doc = await _documentRepository.GetAsync(userId, documentId);
            if (doc == null)
            {
                throw new KeyNotFoundException($"Document {documentId} not found or access denied.");
            }

            allowed.Add(documentId);
        }

        return allowed;
    }
}

using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IDocumentRepository
{
    Task CreateAsync(KnowledgeDocument doc);
    Task<KnowledgeDocument?> GetAsync(string userId, string documentId);
    Task<List<KnowledgeDocument>> ListAsync(string userId, int limit, string? cursor);
    Task UpdateAsync(KnowledgeDocument doc);
    Task DeleteAsync(string userId, string documentId);
}

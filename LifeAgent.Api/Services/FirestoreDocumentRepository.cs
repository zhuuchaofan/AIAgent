using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class FirestoreDocumentRepository : IDocumentRepository
{
    private readonly FirestoreDb _db;

    public FirestoreDocumentRepository(FirestoreDb db)
    {
        _db = db;
    }

    private CollectionReference GetCollection(string userId)
    {
        return _db.Collection("users").Document(userId).Collection("documents");
    }

    public async Task CreateAsync(KnowledgeDocument doc)
    {
        var docRef = GetCollection(doc.UserId).Document(doc.Id);
        await docRef.SetAsync(doc);
    }

    public async Task<KnowledgeDocument?> GetAsync(string userId, string documentId)
    {
        var docRef = GetCollection(userId).Document(documentId);
        var snapshot = await docRef.GetSnapshotAsync();
        return snapshot.Exists ? snapshot.ConvertTo<KnowledgeDocument>() : null;
    }

    public async Task<List<KnowledgeDocument>> ListAsync(string userId, int limit, string? cursor)
    {
        var query = GetCollection(userId)
            .OrderByDescending("createdAt")
            .Limit(limit);

        var snapshot = await query.GetSnapshotAsync();
        return snapshot.Documents.Select(d => d.ConvertTo<KnowledgeDocument>()).ToList();
    }

    public async Task UpdateAsync(KnowledgeDocument doc)
    {
        var docRef = GetCollection(doc.UserId).Document(doc.Id);
        await docRef.SetAsync(doc);
    }

    public async Task DeleteAsync(string userId, string documentId)
    {
        var docRef = GetCollection(userId).Document(documentId);
        await docRef.DeleteAsync();
    }
}

using Google.Cloud.Firestore;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services.LifeEvents;

public class FirestoreAgentLifeEventWriter : IAgentLifeEventWriter
{
    private readonly FirestoreDb _db;

    public FirestoreAgentLifeEventWriter(FirestoreDb db)
    {
        _db = db;
    }

    public async Task WriteAsync(
        string authenticatedUserId,
        string eventId,
        LifeEvent lifeEvent,
        CancellationToken cancellationToken = default)
    {
        var docRef = _db.Collection("users")
            .Document(authenticatedUserId)
            .Collection("life_events")
            .Document(eventId);

        await docRef.SetAsync(lifeEvent, cancellationToken: cancellationToken);
    }
}

using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Services.Agent.PendingActions;

public static class PendingActionStoreFactory
{
    public static IPendingActionStore Create(
        IOptions<PendingActionPersistenceOptions> options,
        FirestoreDb firestoreDb,
        TimeProvider timeProvider)
    {
        return Create(options.Value, () => firestoreDb, timeProvider);
    }

    internal static IPendingActionStore Create(
        PendingActionPersistenceOptions options,
        Func<FirestoreDb> firestoreDbFactory,
        TimeProvider timeProvider)
    {
        return options.UseFirestore
            ? new FirestorePendingActionStore(firestoreDbFactory(), timeProvider)
            : new InMemoryPendingActionStore(timeProvider);
    }
}

namespace LifeAgent.Api.Services;

public interface ICloudTasksService
{
    Task EnqueueIngestTaskAsync(string userId, string documentId, string gcsPath);
}

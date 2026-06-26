namespace LifeAgent.Api.Models;

public class RagOptions
{
    public const string Rag = "Rag";

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";
    public int EmbeddingDimension { get; set; } = 768;
    public double DistanceThreshold { get; set; } = 0.35;
    public int TopK { get; set; } = 5;
    public int MaxFileSizeMb { get; set; } = 10;
    public string GcsBucketName { get; set; } = "";
    public string CloudTasksQueueName { get; set; } = "";
    public string CloudTasksLocation { get; set; } = "";
    public string InternalProcessAudience { get; set; } = "";
    public string CloudTasksServiceAccountEmail { get; set; } = "";
}

namespace LifeAgent.Api.Models;

public class RagOptions
{
    public const string Rag = "Rag";

    public string EmbeddingModel { get; set; } = "gemini-embedding-001";
    public int EmbeddingDimension { get; set; } = 768;
    public double DistanceThreshold { get; set; } = 0.35;
    public int TopK { get; set; } = 5;
    public int MaxFileSizeMb { get; set; } = 10;
    public int MaxChunksPerDocument { get; set; } = 200;

    /// <summary>
    /// 后端请求体大小上限（MB），作为文件上传的安全兜底（默认 15）。
    /// </summary>
    public int MaxRequestBodySizeMb { get; set; } = 15;

    /// <summary>每用户每日 Gemini LLM 调用上限（默认 200）。</summary>
    public int DailyLlmCallLimit { get; set; } = 200;

    /// <summary>每用户每日 Gemini Embedding 调用上限（默认 500）。</summary>
    public int DailyEmbeddingCallLimit { get; set; } = 500;

    /// <summary>每用户每日文档处理数量上限（默认 20）。</summary>
    public int DailyDocumentProcessLimit { get; set; } = 20;

    public string GcsBucketName { get; set; } = "";
    public string CloudTasksQueueName { get; set; } = "";
    public string CloudTasksLocation { get; set; } = "";
    public string InternalProcessAudience { get; set; } = "";
    public string CloudTasksServiceAccountEmail { get; set; } = "";
}

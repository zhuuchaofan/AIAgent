using Google.Cloud.Storage.V1;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class GoogleCloudStorageService : ICloudStorageService
{
    private readonly StorageClient _storageClient;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<GoogleCloudStorageService> _logger;

    public GoogleCloudStorageService(
        IOptions<RagOptions> ragOptions,
        ILogger<GoogleCloudStorageService> logger,
        StorageClient? storageClient = null)
    {
        _ragOptions = ragOptions.Value;
        _logger = logger;
        // 如果外部未传入（如真实运行环境下），从 ADC 获取默认客户端；如果传入（如测试环境），则使用 Mock 客户端
        _storageClient = storageClient ?? StorageClient.Create();
    }

    public async Task<string> UploadFileAsync(string userId, string documentId, string fileName, Stream fileStream, string mimeType)
    {
        var bucketName = _ragOptions.GcsBucketName;
        if (string.IsNullOrEmpty(bucketName))
        {
            throw new InvalidOperationException("GCS BucketName is not configured in RagOptions.");
        }

        var objectPath = BuildGcsObjectPath(userId, documentId, fileName);
        _logger.LogInformation("Uploading file to GCS: gs://{Bucket}/{Object} (MIME: {Mime})", bucketName, objectPath, mimeType);

        await _storageClient.UploadObjectAsync(bucketName, objectPath, mimeType, fileStream);

        var fullGcsPath = $"gs://{bucketName}/{objectPath}";
        _logger.LogInformation("Successfully uploaded file to GCS: {Path}", fullGcsPath);
        return fullGcsPath;
    }

    public async Task<Stream> DownloadFileAsync(string userId, string documentId, string fileName)
    {
        var bucketName = _ragOptions.GcsBucketName;
        if (string.IsNullOrEmpty(bucketName))
        {
            throw new InvalidOperationException("GCS BucketName is not configured.");
        }

        var objectPath = BuildGcsObjectPath(userId, documentId, fileName);
        _logger.LogInformation("Downloading file from GCS: gs://{Bucket}/{Object}", bucketName, objectPath);

        var memoryStream = new MemoryStream();
        await _storageClient.DownloadObjectAsync(bucketName, objectPath, memoryStream);
        memoryStream.Position = 0; // 重置流位置以便调用方读取
        return memoryStream;
    }

    public async Task<Stream> DownloadFileByPathAsync(string gcsPath)
    {
        var (bucketName, objectPath) = ParseGcsPath(gcsPath);
        _logger.LogInformation("Downloading file from GCS path: gs://{Bucket}/{Object}", bucketName, objectPath);

        var memoryStream = new MemoryStream();
        await _storageClient.DownloadObjectAsync(bucketName, objectPath, memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task DeleteFileByPathAsync(string gcsPath)
    {
        var (bucketName, objectPath) = ParseGcsPath(gcsPath);
        _logger.LogInformation("Deleting object from GCS: gs://{Bucket}/{Object}", bucketName, objectPath);

        await _storageClient.DeleteObjectAsync(bucketName, objectPath);
        _logger.LogInformation("Successfully deleted object: gs://{Bucket}/{Object}", bucketName, objectPath);
    }

    // ────────────────────────────────────────────────────────────────────────
    // 静态辅助方法 (便于单元测试验证路径生成与解析逻辑)
    // ────────────────────────────────────────────────────────────────────────

    public static string BuildGcsObjectPath(string userId, string documentId, string fileName)
    {
        var safeFileName = GetSafeFileName(fileName);
        return $"users/{userId}/documents/{documentId}/{safeFileName}";
    }

    public static string GetSafeFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "unnamed";
        
        // 1. 去掉路径信息，防范目录穿越漏洞 (Directory Traversal)
        var basename = Path.GetFileName(fileName);
        
        // 2. URL 编码，安全转义空格、中文与特殊字符
        return Uri.EscapeDataString(basename);
    }

    public static (string bucket, string objectPath) ParseGcsPath(string gcsPath)
    {
        if (string.IsNullOrEmpty(gcsPath) || !gcsPath.StartsWith("gs://"))
        {
            throw new ArgumentException("Invalid GCS path format. Must start with gs://", nameof(gcsPath));
        }

        var pathWithoutScheme = gcsPath.Substring(5);
        var firstSlash = pathWithoutScheme.IndexOf('/');
        if (firstSlash <= 0)
        {
            throw new ArgumentException("Invalid GCS path format. Could not parse bucket name.", nameof(gcsPath));
        }

        var bucket = pathWithoutScheme.Substring(0, firstSlash);
        var objectPath = pathWithoutScheme.Substring(firstSlash + 1);

        if (string.IsNullOrEmpty(objectPath))
        {
            throw new ArgumentException("Invalid GCS path format. Object path is empty.", nameof(gcsPath));
        }

        return (bucket, objectPath);
    }
}

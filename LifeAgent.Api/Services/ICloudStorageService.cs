namespace LifeAgent.Api.Services;

public interface ICloudStorageService
{
    Task<string> UploadFileAsync(string userId, string documentId, string fileName, Stream fileStream, string mimeType);
    Task<Stream> DownloadFileAsync(string userId, string documentId, string fileName);
    Task<Stream> DownloadFileByPathAsync(string gcsPath);
    Task DeleteFileByPathAsync(string gcsPath);
}

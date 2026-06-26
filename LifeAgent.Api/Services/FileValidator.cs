using Microsoft.Extensions.Options;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class FileValidator
{
    private readonly RagOptions _ragOptions;

    public FileValidator(IOptions<RagOptions> ragOptions)
    {
        _ragOptions = ragOptions.Value;
    }

    public (bool IsValid, string? ErrorMessage) ValidateFile(string fileName, long fileSize, string mimeType)
    {
        // 1. 校验大小
        var maxSizeBytes = _ragOptions.MaxFileSizeMb * 1024L * 1024L;
        if (fileSize > maxSizeBytes)
        {
            return (false, $"File size ({fileSize} bytes) exceeds the maximum allowed limit of {_ragOptions.MaxFileSizeMb}MB.");
        }

        // 2. 校验文件后缀名
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowedExtensions = new[] { ".pdf", ".txt", ".md" };
        if (!allowedExtensions.Contains(ext))
        {
            return (false, $"Unsupported file extension '{ext}'. Only .pdf, .txt, and .md files are supported.");
        }

        // 3. 校验 MIME 类型
        var allowedMimeTypes = new[] { "application/pdf", "text/plain", "text/markdown" };
        var lowerMime = mimeType.ToLowerInvariant();
        
        // 宽松校验：如果后缀是 .md 或 .txt，但有些环境将其识别为 application/octet-stream，我们也予以放行，
        // 否则如果在测试中严格匹配也可以。这里我们写最安全的严格匹配，并对 text/markdown / text/plain / application/pdf 做严格校验。
        if (!allowedMimeTypes.Contains(lowerMime))
        {
            return (false, $"Unsupported MIME type '{mimeType}'. Supported types are application/pdf, text/plain, text/markdown.");
        }

        return (true, null);
    }
}

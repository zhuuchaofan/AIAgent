using System.Text;
using UglyToad.PdfPig;
using Microsoft.Extensions.Logging;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class PdfPigDocumentTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PdfPigDocumentTextExtractor> _logger;

    public PdfPigDocumentTextExtractor(ILogger<PdfPigDocumentTextExtractor> logger)
    {
        _logger = logger;
    }

    public async Task<ExtractionResult> ExtractTextAsync(Stream stream, string mimeType)
    {
        var result = new ExtractionResult();
        try
        {
            if (stream == null)
            {
                result.Success = false;
                result.ErrorMessage = "Input stream is null.";
                return result;
            }

            var lowerMime = mimeType.ToLowerInvariant();
            if (lowerMime == "text/plain" || lowerMime == "text/markdown")
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
                var rawText = await reader.ReadToEndAsync();
                
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    result.Success = false;
                    result.ErrorMessage = "The text file is empty.";
                    return result;
                }

                result.Success = true;
                result.RawText = rawText;
                result.Pages.Add(new PageTextInfo
                {
                    PageNumber = 1,
                    Text = rawText,
                    CharStart = 0,
                    CharEnd = rawText.Length
                });
            }
            else if (lowerMime == "application/pdf")
            {
                // 将 Stream 读入 MemoryStream，防止 Stream 不支持 Seek 导致 PdfPig 解析崩溃
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();

                if (bytes.Length == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "The PDF file is empty (0 bytes).";
                    return result;
                }

                var fullTextBuilder = new StringBuilder();
                var pagesInfo = new List<PageTextInfo>();

                // PdfDocument.Open 可能会因为文件损坏或加密抛出异常
                using (var pdf = PdfDocument.Open(bytes))
                {
                    if (pdf.IsEncrypted)
                    {
                        result.Success = false;
                        result.ErrorMessage = "The PDF file is encrypted and cannot be parsed.";
                        return result;
                    }

                    var totalPages = pdf.NumberOfPages;
                    for (int pageNum = 1; pageNum <= totalPages; pageNum++)
                    {
                        var page = pdf.GetPage(pageNum);
                        var pageText = page.Text ?? "";
                        
                        var charStart = fullTextBuilder.Length;
                        fullTextBuilder.Append(pageText);
                        var charEnd = fullTextBuilder.Length;

                        // PDF 换页累加换行以维持自然段落
                        fullTextBuilder.Append('\n');

                        pagesInfo.Add(new PageTextInfo
                        {
                            PageNumber = pageNum,
                            Text = pageText,
                            CharStart = charStart,
                            CharEnd = charEnd
                        });
                    }
                }

                var rawText = fullTextBuilder.ToString();
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    result.Success = false;
                    result.ErrorMessage = "No text content could be extracted from this PDF.";
                    return result;
                }

                result.Success = true;
                result.RawText = rawText;
                result.Pages = pagesInfo;
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = $"Unsupported MIME type for text extraction: '{mimeType}'.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during document text extraction.");
            result.Success = false;
            
            var isPdfError = ex.Message.Contains("PDF", StringComparison.OrdinalIgnoreCase) || 
                             ex.GetType().FullName?.Contains("Pdf", StringComparison.OrdinalIgnoreCase) == true;
                             
            if (isPdfError)
            {
                result.ErrorMessage = "Failed to parse PDF: The file format is invalid, encrypted or corrupted.";
            }
            else
            {
                result.ErrorMessage = $"An error occurred during text extraction: {ex.Message}";
            }
        }

        return result;
    }
}

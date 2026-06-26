using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public interface IDocumentTextExtractor
{
    Task<ExtractionResult> ExtractTextAsync(Stream stream, string mimeType);
}

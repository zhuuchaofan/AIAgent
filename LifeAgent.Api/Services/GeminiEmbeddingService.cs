using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeAgent.Api.Services;

public class GeminiEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiEmbeddingService> _logger;
    private readonly string _apiKey;

    public GeminiEmbeddingService(HttpClient httpClient, IConfiguration config, ILogger<GeminiEmbeddingService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var configKey = _config["Gemini:ApiKey"];
        _apiKey = !string.IsNullOrEmpty(configKey)
            ? configKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new float[768];
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={_apiKey}";
        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new
            {
                parts = new[] { new { text = text } }
            },
            outputDimensionality = 768
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini Embedding API failed: {Status} - {Err}", response.StatusCode, err);
            throw new HttpRequestException($"Gemini Embedding API failed with status {response.StatusCode}: {err}");
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        
        if (root.TryGetProperty("embedding", out var embeddingNode) && 
            embeddingNode.TryGetProperty("values", out var valuesNode))
        {
            var len = valuesNode.GetArrayLength();
            if (len != 768)
            {
                throw new InvalidOperationException($"Expected embedding dimension of 768, but received {len}.");
            }

            var vec = new float[768];
            var i = 0;
            foreach (var val in valuesNode.EnumerateArray())
            {
                vec[i++] = (float)val.GetDouble();
            }

            return vec;
        }

        throw new InvalidOperationException("Failed to parse embedding values from response payload.");
    }
}

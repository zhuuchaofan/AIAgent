using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class GeminiRagAnswerGenerator : IRagAnswerGenerator
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiRagAnswerGenerator> _logger;
    private readonly string _apiKey;

    public GeminiRagAnswerGenerator(HttpClient httpClient, IConfiguration config, ILogger<GeminiRagAnswerGenerator> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;

        var configKey = _config["Gemini:ApiKey"];
        _apiKey = !string.IsNullOrEmpty(configKey)
            ? configKey
            : Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
    }

    public async Task<string> GenerateAnswerAsync(string systemInstruction, string userPrompt, List<ChatMessage> history)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Gemini API key is not configured.");
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

        var contentsList = new List<object>();
        if (history != null)
        {
            foreach (var msg in history)
            {
                var role = msg.Role.ToLowerInvariant() == "assistant" ? "model" : "user";
                contentsList.Add(new
                {
                    role = role,
                    parts = new[] { new { text = msg.Content } }
                });
            }
        }

        contentsList.Add(new
        {
            role = "user",
            parts = new[] { new { text = userPrompt } }
        });

        var requestBody = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemInstruction } }
            },
            contents = contentsList
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            _logger.LogError("Gemini RAG Generate Content failed: {Status} - {Err}", response.StatusCode, err);
            throw new HttpRequestException($"Gemini RAG API failed with status {response.StatusCode}: {err}");
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out var contentNode) &&
            contentNode.TryGetProperty("parts", out var parts) &&
            parts.GetArrayLength() > 0 &&
            parts[0].TryGetProperty("text", out var textNode))
        {
            return textNode.GetString() ?? "";
        }

        throw new InvalidOperationException("Failed to parse RAG answer text from Gemini response payload.");
    }
}

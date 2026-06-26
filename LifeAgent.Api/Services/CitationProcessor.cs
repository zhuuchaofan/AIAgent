using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class CitationProcessor
{
    private static readonly Regex CitationRegex = new(@"\[([1-9][0-9]?)\]", RegexOptions.Compiled);

    public static (string CleanedResponse, string CitationIntegrity, List<CitationNode> Citations) Process(
        string rawResponse,
        List<KnowledgeChunk> retrievedChunks)
    {
        bool isRefusal = rawResponse.Contains("抱歉，在您上传的个人资料中") ||
                         rawResponse.Contains("资料中未找到足够依据回答该问题");

        var matches = CitationRegex.Matches(rawResponse);
        
        if (matches.Count == 0)
        {
            var integrity = isRefusal ? "valid" : "missing";
            return (rawResponse, integrity, new List<CitationNode>());
        }

        bool hasInvalid = false;
        var validIndices = new HashSet<int>();

        string cleanedResponse = CitationRegex.Replace(rawResponse, m =>
        {
            var numStr = m.Groups[1].Value;
            if (int.TryParse(numStr, out int index))
            {
                if (index > 0 && index <= retrievedChunks.Count)
                {
                    validIndices.Add(index);
                    return m.Value;
                }
            }
            hasInvalid = true;
            return "";
        });

        var citations = new List<CitationNode>();
        foreach (var idx in validIndices)
        {
            var chunk = retrievedChunks[idx - 1];
            citations.Add(new CitationNode
            {
                Index = idx,
                DocumentId = chunk.DocumentId,
                DocumentName = chunk.DocumentName,
                ChunkIndex = chunk.ChunkIndex,
                PageNumber = chunk.PageNumber,
                SectionTitle = chunk.SectionTitle,
                SnippetPreview = chunk.Content.Length > 150 ? chunk.Content.Substring(0, 150) + "..." : chunk.Content
            });
        }

        citations.Sort((a, b) => a.Index.CompareTo(b.Index));

        string finalIntegrity;
        if (hasInvalid)
        {
            finalIntegrity = "invalid_cleaned";
        }
        else
        {
            if (rawResponse.Contains("部分") || rawResponse.Contains("partial"))
            {
                finalIntegrity = "partial";
            }
            else
            {
                finalIntegrity = "valid";
            }
        }

        return (cleanedResponse, finalIntegrity, citations);
    }
}

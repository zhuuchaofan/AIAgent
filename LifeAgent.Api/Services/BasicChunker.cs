using System.Text;
using LifeAgent.Api.Models;

namespace LifeAgent.Api.Services;

public class BasicChunker : IChunker
{
    private const int TargetSize = 800;
    private const int OverlapSize = 80;

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text)
    {
        return SplitDocument(userId, documentId, documentName, text, maxChunks: int.MaxValue);
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, string text, int maxChunks)
    {
        var pages = new List<PageTextInfo>
        {
            new PageTextInfo
            {
                PageNumber = 1,
                Text = text ?? "",
                CharStart = 0,
                CharEnd = text?.Length ?? 0
            }
        };
        return SplitDocument(userId, documentId, documentName, pages, maxChunks);
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages)
    {
        return SplitDocument(userId, documentId, documentName, pages, maxChunks: int.MaxValue);
    }

    public List<KnowledgeChunk> SplitDocument(string userId, string documentId, string documentName, List<PageTextInfo> pages, int maxChunks)
    {
        var chunks = new List<KnowledgeChunk>();
        if (pages == null || pages.Count == 0) return chunks;

        var chunkIndex = 0;

        foreach (var page in pages)
        {
            var text = page.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;

            var paragraphItems = GetParagraphsWithOffsets(text, page.CharStart);
            if (paragraphItems.Count == 0) continue;

            var currentChunkText = new StringBuilder();
            var currentChunkStart = -1;
            var currentChunkEnd = -1;

            for (int i = 0; i < paragraphItems.Count; i++)
            {
                var item = paragraphItems[i];

                if (currentChunkText.Length == 0)
                {
                    currentChunkStart = item.Start;
                }

                if (currentChunkText.Length > 0 && !currentChunkText.ToString().EndsWith("\n"))
                {
                    currentChunkText.Append('\n');
                }
                currentChunkText.Append(item.Text);
                currentChunkEnd = item.End;

                if (currentChunkText.Length >= TargetSize)
                {
                    var chunkContent = currentChunkText.ToString();
                    chunks.Add(CreateChunk(userId, documentId, documentName, chunkIndex++, page.PageNumber, currentChunkStart, currentChunkEnd, chunkContent));

                    if (chunks.Count >= maxChunks)
                    {
                        return chunks;
                    }

                    var overlapText = GetOverlapText(chunkContent, OverlapSize);
                    currentChunkText.Clear();
                    currentChunkText.Append(overlapText);
                    currentChunkStart = Math.Max(page.CharStart, currentChunkEnd - overlapText.Length);
                }
            }

            if (currentChunkText.Length > 0)
            {
                var remainingText = currentChunkText.ToString();
                if (!string.IsNullOrWhiteSpace(remainingText))
                {
                    chunks.Add(CreateChunk(userId, documentId, documentName, chunkIndex++, page.PageNumber, currentChunkStart, currentChunkEnd, remainingText));

                    if (chunks.Count >= maxChunks)
                    {
                        return chunks;
                    }
                }
            }
        }

        return chunks;
    }

    private static KnowledgeChunk CreateChunk(
        string userId,
        string documentId,
        string documentName,
        int chunkIndex,
        int pageNumber,
        int charStart,
        int charEnd,
        string content)
    {
        return new KnowledgeChunk
        {
            Id = $"{documentId}_{chunkIndex}",
            UserId = userId,
            DocumentId = documentId,
            DocumentName = documentName,
            ChunkIndex = chunkIndex,
            PageNumber = pageNumber,
            CharStart = charStart,
            CharEnd = charEnd,
            Content = content,
            CreatedAt = DateTime.UtcNow
        };
    }

    private class ParagraphItem
    {
        public string Text { get; set; } = "";
        public int Start { get; set; }
        public int End { get; set; }
    }

    private static List<ParagraphItem> GetParagraphsWithOffsets(string text, int pageCharStart)
    {
        var list = new List<ParagraphItem>();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var currentOffsetInPage = 0;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                var startInPage = text.IndexOf(line, currentOffsetInPage);
                if (startInPage < 0) startInPage = currentOffsetInPage;

                list.Add(new ParagraphItem
                {
                    Text = trimmed,
                    Start = pageCharStart + startInPage,
                    End = pageCharStart + startInPage + line.Length
                });
                
                currentOffsetInPage = startInPage + line.Length;
            }
            else
            {
                currentOffsetInPage += line.Length + 1;
            }
        }
        return list;
    }

    private static string GetOverlapText(string text, int overlapSize)
    {
        if (text.Length <= overlapSize) return text;

        var startIdx = text.Length - overlapSize;
        var puncs = new[] { '。', '！', '？', '；', '、', '.', '!', '?', ';', ',', '，', '\n' };

        for (int offset = 0; offset < 15; offset++)
        {
            if (startIdx + offset < text.Length && puncs.Contains(text[startIdx + offset]))
            {
                return text.Substring(startIdx + offset + 1);
            }
            if (startIdx - offset >= 0 && puncs.Contains(text[startIdx - offset]))
            {
                return text.Substring(startIdx - offset + 1);
            }
        }

        return text.Substring(startIdx);
    }
}

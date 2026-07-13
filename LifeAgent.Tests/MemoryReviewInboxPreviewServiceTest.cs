using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Tests;

public class MemoryReviewInboxPreviewServiceTest
{
    private readonly MemoryReviewInboxPreviewService _service = new();

    [Fact]
    public void BuildPreview_RequiresUserId()
    {
        Assert.Throws<ArgumentException>(() => _service.BuildPreview("", Array.Empty<LifeEvent>()));
    }

    [Fact]
    public void BuildPreview_ReturnsPreviewOnlyCandidatesWithoutWriteFlags()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_brand", "户外品牌", "种草了 kolon sports，版型好看，但是价格很贵。"),
            NewEvent("evt_project", "整理项目", "最近让 codex 整理 LifeOS 项目。")
        });

        Assert.True(preview.PreviewOnly);
        Assert.False(preview.WroteData);
        Assert.Equal(2, preview.ScannedCount);
        Assert.Contains(preview.Candidates, candidate =>
            candidate.Id == "review_sportswear_price" &&
            candidate.Type == "preference" &&
            candidate.Title == "运动服饰和价格权衡" &&
            candidate.PreviewOnly &&
            !candidate.WroteData);
        Assert.Contains(preview.Candidates, candidate =>
            candidate.Id == "review_project_work" &&
            candidate.Type == "theme");
    }

    [Fact]
    public void BuildPreview_MergesRepeatedCandidates()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_project_1", "整理项目", "继续整理 LifeOS。", minutesAgo: 10),
            NewEvent("evt_project_2", "Codex", "让 codex 整理项目。", minutesAgo: 5)
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("review_project_work", candidate.Id);
        Assert.Equal("最近多次出现", candidate.Reason);
        Assert.Equal(new[] { "evt_project_2", "evt_project_1" }, candidate.SourceEventIds);
    }

    [Fact]
    public void BuildPreview_ReturnsEmptyCandidatesWhenNoSignalExists()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_plain", "普通记录", "今天买了咖啡。")
        });

        Assert.Empty(preview.Candidates);
        Assert.Equal(1, preview.ScannedCount);
    }

    private static LifeEvent NewEvent(string id, string title, string content, int minutesAgo = 0)
    {
        return new LifeEvent
        {
            Id = id,
            Type = "life",
            Title = title,
            Content = content,
            OccurredAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
            Tags = new List<string> { "生活日常" },
            Importance = 1
        };
    }
}

using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Tests;

public class MemoryContextPreviewServiceTest
{
    private readonly MemoryContextPreviewService _service = new(new MemoryReviewInboxPreviewService());

    [Fact]
    public void BuildPreview_RequiresUserId()
    {
        Assert.Throws<ArgumentException>(() => _service.BuildPreview("", Array.Empty<LifeEvent>()));
    }

    [Fact]
    public void BuildPreview_ReturnsReadOnlyFlags()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_xinjiang", "新疆路上", "下周应该就在去新疆的路上啦。")
        });

        Assert.True(preview.PreviewOnly);
        Assert.False(preview.WroteData);
        Assert.False(preview.MemoryWriteEnabled);
        Assert.Equal(1, preview.ScannedCount);
        var item = Assert.Single(preview.Items);
        Assert.Equal("temporary_context", item.Kind);
        Assert.Equal("你近期有去新疆的出行计划。", item.Text);
        Assert.Equal("observing", item.ReviewStage);
    }

    [Fact]
    public void BuildPreview_PrioritizesStableSignals()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_xinjiang", "新疆路上", "下周应该就在去新疆的路上啦。", minutesAgo: 1),
            NewEvent("evt_bike_1", "骑车", "今天骑车，心率不高。", minutesAgo: 20),
            NewEvent("evt_bike_2", "运动", "继续记录运动和身体状态。", minutesAgo: 10)
        });

        Assert.NotEmpty(preview.Items);
        Assert.Equal("stable", preview.Items[0].ReviewStage);
        Assert.Equal("你会关注运动状态和身体感受。", preview.Items[0].Text);
    }

    [Fact]
    public void BuildPromptSection_LabelsContextAsReadOnlyAndNonExecutable()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_project_1", "整理 LifeOS", "继续整理项目。", minutesAgo: 20),
            NewEvent("evt_project_2", "Codex", "让 codex 整理项目。", minutesAgo: 10)
        });

        var section = MemoryContextPreviewService.BuildPromptSection(preview);

        Assert.Contains("只读生活线索 Context Preview", section);
        Assert.Contains("不得替代 Chunks", section);
        Assert.Contains("不得据此执行任何保存、提醒或工具操作", section);
        Assert.Contains("更稳定 / theme", section);
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

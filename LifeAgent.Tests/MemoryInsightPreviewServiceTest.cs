using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Memories;

namespace LifeAgent.Tests;

public class MemoryInsightPreviewServiceTest
{
    private readonly MemoryInsightPreviewService _service = new(new MemoryExtractionService(new MemoryProposalGuard()));

    [Fact]
    public void BuildPreview_RequiresUserId()
    {
        Assert.Throws<ArgumentException>(() => _service.BuildPreview("", Array.Empty<LifeEvent>()));
    }

    [Fact]
    public void BuildPreview_ReturnsReadOnlySafetyFlags()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_1", "偏好", "我喜欢早上骑车")
        });

        Assert.True(preview.PreviewOnly);
        Assert.False(preview.WroteData);
        Assert.False(preview.MemoryWriteEnabled);
        Assert.Equal(1, preview.ScannedCount);
        Assert.Single(preview.Insights);
    }

    [Fact]
    public void BuildPreview_MapsHabitGoalAndPreferenceToUserReadableInsights()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_preference", "偏好", "我喜欢早上骑车"),
            NewEvent("evt_habit", "习惯", "我每天晚上整理桌面"),
            NewEvent("evt_goal", "目标", "我的目标是本月完成 LifeOS Memory Engine")
        });

        Assert.Equal(3, preview.Insights.Count);
        Assert.Contains(preview.Insights, insight =>
            insight.Kind == "preference" &&
            insight.Text.Contains("可能的偏好", StringComparison.Ordinal));
        Assert.Contains(preview.Insights, insight =>
            insight.Kind == "habit" &&
            insight.Text.Contains("正在形成的习惯", StringComparison.Ordinal));
        Assert.Contains(preview.Insights, insight =>
            insight.Kind == "goal" &&
            insight.Text.Contains("目标或计划", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPreview_DoesNotExposeSkippedOrRejectedCandidates()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_trivial", "普通记录", "今天买了咖啡"),
            NewEvent("evt_rejected", "临时背景", "本周出差，缺少过期时间")
        });

        Assert.Empty(preview.Insights);
        Assert.Equal(2, preview.ScannedCount);
    }

    [Fact]
    public void BuildPreview_CleansTechnicalWriteText()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent(
                "evt_dirty",
                "项目",
                "用户输入：最近整理 LifeOS 项目。生活记录确认后写入 life_events；提醒与工具操作仍不执行。")
        });

        var insight = Assert.Single(preview.Insights);
        Assert.DoesNotContain("life_events", insight.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("工具操作", insight.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPreview_DoesNotRepeatTitleAndContentInThemeInsight()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent(
                "evt_project",
                "哈，最近让 codex 整理项目，一直整理，一直一团糟糕～",
                "哈，最近让 codex 整理项目，一直整理，一直一团糟糕～")
        });

        var insight = Assert.Single(preview.Insights);
        Assert.Equal("theme", insight.Kind);
        Assert.Equal("你最近在持续整理项目相关的事情。", insight.Text);
    }

    [Fact]
    public void BuildPreview_CondensesLongThemeInsightInsteadOfEchoingOriginalRecord()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent(
                "evt_trip",
                "西安国家版本馆无人机首飞",
                "今天和朋友开车一起去西安国家版本馆飞了无人机，第一次飞，很不错，拍了几段视频，虽然毫无章法，但是基本掌握了飞行的基础。")
        });

        var insight = Assert.Single(preview.Insights);
        Assert.Equal("你最近在记录出行和新体验。", insight.Text);
        Assert.DoesNotContain("第一次飞，很不错", insight.Text, StringComparison.Ordinal);
    }

    private static LifeEvent NewEvent(string id, string title, string content)
    {
        return new LifeEvent
        {
            Id = id,
            Type = "life",
            Title = title,
            Content = content,
            OccurredAt = DateTime.UtcNow,
            Tags = new List<string> { "生活日常" },
            Importance = 1
        };
    }
}

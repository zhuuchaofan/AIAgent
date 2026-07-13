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
    public void BuildPreview_AggregatesSimilarRecordsIntoUserReadableInsights()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_preference", "偏好", "我喜欢早上骑车"),
            NewEvent("evt_habit", "习惯", "我每天晚上整理桌面"),
            NewEvent("evt_goal", "目标", "我的目标是本月完成 LifeOS Memory Engine")
        });

        Assert.Equal(2, preview.Insights.Count);
        Assert.Contains(preview.Insights, insight =>
            insight.Kind == "theme" &&
            insight.Text == "你最近在关注运动状态和身体感受。");
        Assert.Contains(preview.Insights, insight =>
            insight.Kind == "theme" &&
            insight.Text == "你最近在持续整理项目相关的事情。");
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
    public void BuildPreview_MergesMultipleRecordsForSameTheme()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_project_1", "整理 LifeOS", "今天继续整理项目结构。", minutesAgo: 30),
            NewEvent("evt_project_2", "Codex 协助", "让 codex 继续整理项目文档。", minutesAgo: 20),
            NewEvent("evt_project_3", "项目收口", "晚上又整理了一轮 LifeOS。", minutesAgo: 10)
        });

        var insight = Assert.Single(preview.Insights);
        Assert.Equal("你最近在持续整理项目相关的事情。", insight.Text);
        Assert.Equal(new[] { "evt_project_3", "evt_project_2", "evt_project_1" }, insight.SourceEventIds);
    }

    [Fact]
    public void BuildPreview_ReturnsAtMostThreeAggregatedInsights()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_project_1", "整理项目", "今天整理 LifeOS 项目。", minutesAgo: 50),
            NewEvent("evt_project_2", "继续整理", "继续让 codex 整理项目。", minutesAgo: 40),
            NewEvent("evt_trip", "出行", "今天在新疆路上记录新的体验。", minutesAgo: 30),
            NewEvent("evt_food", "美食", "晚上吃了饺子，也有一点支出。", minutesAgo: 20),
            NewEvent("evt_bike", "骑行", "今天骑车，心率不高。", minutesAgo: 10),
            NewEvent("evt_price", "价格", "像版型很好看，但是价格很贵。", minutesAgo: 5)
        });

        Assert.Equal(3, preview.Insights.Count);
        Assert.Contains(preview.Insights, insight => insight.Text == "你最近在持续整理项目相关的事情。");
        Assert.Contains(preview.Insights, insight => insight.Text == "你最近在关注运动服饰，也在权衡价格。");
    }

    [Fact]
    public void BuildPreview_RecentVisibleRecordsOutrankOlderFrequentThemes()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_brand", "kolon sports", "种草了一个户外品牌，版型很好看，但是价格很贵。", minutesAgo: 1),
            NewEvent("evt_xinjiang", "新疆路上", "下周应该就在去新疆的路上啦。", minutesAgo: 2),
            NewEvent("evt_codex_recent", "整理项目", "最近让 codex 整理项目。", minutesAgo: 3),
            NewEvent("evt_food_old", "美食", "晚上吃了饺子，也有一点支出。", minutesAgo: 40),
            NewEvent("evt_bike_old_1", "骑行", "今天骑车，心率不高。", minutesAgo: 50),
            NewEvent("evt_bike_old_2", "骑行", "继续记录运动和身体状态。", minutesAgo: 60)
        });

        Assert.Equal(3, preview.Insights.Count);
        Assert.Equal("你最近在关注运动服饰，也在权衡价格。", preview.Insights[0].Text);
        Assert.Contains(preview.Insights, insight => insight.Text == "你近期有去新疆的出行计划。");
        Assert.Contains(preview.Insights, insight => insight.Text == "你最近在持续整理项目相关的事情。");
        Assert.DoesNotContain(preview.Insights, insight => insight.Text == "你最近在关注运动状态和身体感受。");
    }

    [Fact]
    public void BuildPreview_UsesConcreteXinjiangTravelInsight()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_xinjiang", "新疆路上", "下周应该就在去新疆的路上啦。")
        });

        var insight = Assert.Single(preview.Insights);
        Assert.Equal("temporary_context", insight.Kind);
        Assert.Equal("你近期有去新疆的出行计划。", insight.Text);
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

    [Fact]
    public void BuildPreview_CondensesLongPreferenceInsightInsteadOfEchoingOriginalRecord()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent(
                "evt_food_spend",
                "美食之夜，略有支出",
                "晚上果然还是不能出去，开玩笑的，不仅仅吃了个饺子，还吃了个蒸饺，味道也不错，还去品忆香买了好多吃的，又是一个支出的晚上。")
        });

        var insight = Assert.Single(preview.Insights);
        Assert.Equal("preference", insight.Kind);
        Assert.Equal("你最近在记录饮食和消费感受。", insight.Text);
        Assert.DoesNotContain("晚上果然还是不能出去", insight.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPreview_FallsBackToMemoryExtractionForShortUnmatchedPreference()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_coffee", "偏好", "我喜欢 morning coffee")
        });

        var insight = Assert.Single(preview.Insights);
        Assert.Equal("preference", insight.Kind);
        Assert.Contains("morning coffee", insight.Text, StringComparison.OrdinalIgnoreCase);
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

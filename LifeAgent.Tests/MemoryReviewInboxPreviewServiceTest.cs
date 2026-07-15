using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Memories;
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
        Assert.False(preview.MemoryWriteEnabled);
        Assert.Equal(2, preview.ScannedCount);
        Assert.Contains(preview.Candidates, candidate =>
            candidate.Id == "review_sportswear_brand_interest" &&
            candidate.Type == "preference" &&
            candidate.Title == "你最近在关注户外/运动服饰品牌。" &&
            candidate.ReviewStage == "observing" &&
            candidate.PreviewOnly &&
            !candidate.WroteData);
        Assert.Contains(preview.Candidates, candidate =>
            candidate.Id == "review_style_price_hesitation" &&
            candidate.Title == "你会在喜欢版型和价格偏高之间犹豫。");
        Assert.Contains(preview.Candidates, candidate =>
            candidate.Id == "review_project_work" &&
            candidate.Type == "theme" &&
            candidate.Title == "你最近在持续整理 LifeOS 项目。");
        Assert.All(preview.Candidates, candidate =>
        {
            Assert.NotEmpty(candidate.Sources);
            Assert.All(candidate.Sources, source =>
            {
                Assert.False(string.IsNullOrWhiteSpace(source.EventId));
                Assert.DoesNotContain("life_events", source.Snippet, StringComparison.OrdinalIgnoreCase);
            });
        });
    }

    [Fact]
    public void StateProjection_KeepsReviewStateAndFiltersDismissedByDefault()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_brand", "户外品牌", "种草了 kolon sports，版型好看，但是价格很贵。"),
            NewEvent("evt_project", "整理项目", "最近让 codex 整理 LifeOS 项目。")
        });
        var reviewedAt = DateTime.UtcNow;
        var states = new Dictionary<string, MemoryReviewStateRecord>(StringComparer.OrdinalIgnoreCase)
        {
            ["review_project_work"] = new("review_project_work", "kept", reviewedAt, reviewedAt),
            ["review_sportswear_brand_interest"] = new("review_sportswear_brand_interest", "dismissed", reviewedAt, reviewedAt)
        };

        var projected = MemoryReviewInboxStateProjection.Apply(preview, states);

        Assert.True(projected.PreviewOnly);
        Assert.False(projected.WroteData);
        Assert.False(projected.MemoryWriteEnabled);
        Assert.DoesNotContain(projected.Candidates, candidate => candidate.Id == "review_sportswear_brand_interest");
        var kept = Assert.Single(projected.Candidates, candidate => candidate.Id == "review_project_work");
        Assert.Equal("kept", kept.ReviewStatus);
        Assert.Equal(reviewedAt, kept.ReviewedAt);
    }

    [Fact]
    public void StateProjection_AppendsPersistedKeptCandidatesThatAreNoLongerGenerated()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_plain", "普通记录", "今天买了咖啡。")
        });
        var reviewedAt = DateTime.UtcNow;
        var keptCandidate = new MemoryReviewCandidateItem
        {
            Id = "review_old_context",
            Type = "temporary_context",
            Title = "你之前留过的一条线索。",
            Detail = "这条线索来自之前的记录。",
            ReviewStatus = "kept",
            ReviewedAt = reviewedAt,
            PreviewOnly = true,
            WroteData = false
        };

        var projected = MemoryReviewInboxStateProjection.AddMissingKeptCandidates(
            preview,
            new[] { keptCandidate });

        var kept = Assert.Single(projected.Candidates);
        Assert.Equal("review_old_context", kept.Id);
        Assert.Equal("kept", kept.ReviewStatus);
        Assert.Equal(reviewedAt, kept.ReviewedAt);
        Assert.False(projected.MemoryWriteEnabled);
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
        Assert.Equal("stable", candidate.ReviewStage);
        Assert.Equal("更稳定", candidate.ReviewStageLabel);
        Assert.Equal(0.86, candidate.Confidence);
        Assert.Equal(new[] { "evt_project_2", "evt_project_1" }, candidate.SourceEventIds);
        Assert.Equal(new[] { "evt_project_2", "evt_project_1" }, candidate.Sources.Select(source => source.EventId));
    }

    [Fact]
    public void BuildPreview_LimitsSourceSummariesAndCleansTechnicalText()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent(
                "evt_project_1",
                "用户输入：整理 LifeOS",
                "今天继续整理项目结构。生活记录确认后写入 life_events；提醒与工具操作仍不执行。",
                minutesAgo: 30),
            NewEvent("evt_project_2", "Codex 协助", "让 codex 继续整理项目文档。", minutesAgo: 20),
            NewEvent("evt_project_3", "项目收口", "晚上又整理了一轮 LifeOS。", minutesAgo: 10),
            NewEvent("evt_project_4", "继续整理", "又处理了一些项目细节。", minutesAgo: 5)
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal(3, candidate.Sources.Count);
        Assert.DoesNotContain(candidate.Sources, source =>
            source.Snippet.Contains("life_events", StringComparison.OrdinalIgnoreCase) ||
            source.Title.Contains("用户输入", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPreview_GeneratesSpecificXinjiangTravelPlanCandidate()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_xinjiang", "新疆路上", "下周应该就在去新疆的路上啦。")
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("review_xinjiang_travel_plan", candidate.Id);
        Assert.Equal("temporary_context", candidate.Type);
        Assert.Equal("你近期有去新疆的出行计划。", candidate.Title);
        Assert.Equal("observing", candidate.ReviewStage);
        Assert.Equal("观察中", candidate.ReviewStageLabel);
        Assert.Equal(0.72, candidate.Confidence);
        Assert.DoesNotContain("出行和新体验", candidate.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPreview_MarksSingleTravelExperienceAsOneOff()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_trip", "出行", "今天路上去了西安国家版本馆飞无人机。")
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("review_travel_experience", candidate.Id);
        Assert.Equal("temporary_context", candidate.Type);
        Assert.Equal("你最近记录过出行和新体验。", candidate.Title);
        Assert.Equal("one_off", candidate.ReviewStage);
        Assert.Equal("一次性", candidate.ReviewStageLabel);
        Assert.Equal("可能只是一次性事件", candidate.Reason);
        Assert.Equal(0.62, candidate.Confidence);
        Assert.Contains("不建议直接当作长期记忆", candidate.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPreview_TreatsSingleHabitLikeSignalAsRecentContext()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_bike", "骑车", "今天骑车回来，心率不高。")
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("review_body_movement", candidate.Id);
        Assert.Equal("temporary_context", candidate.Type);
        Assert.Equal("你最近记录过运动状态和身体感受。", candidate.Title);
        Assert.Equal("近期线索", candidate.Reason);
        Assert.Contains("更多记录", candidate.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPreview_PromotesRepeatedHabitLikeSignalToStableHabit()
    {
        var preview = _service.BuildPreview("user_a", new[]
        {
            NewEvent("evt_bike_1", "骑车", "今天骑车回来，心率不高。", minutesAgo: 20),
            NewEvent("evt_bike_2", "运动", "继续记录运动和身体状态。", minutesAgo: 10)
        });

        var candidate = Assert.Single(preview.Candidates);
        Assert.Equal("review_body_movement", candidate.Id);
        Assert.Equal("habit", candidate.Type);
        Assert.Equal("你会关注运动状态和身体感受。", candidate.Title);
        Assert.Equal("最近多次出现", candidate.Reason);
        Assert.Equal("stable", candidate.ReviewStage);
        Assert.Contains("较稳定的习惯线索", candidate.Detail, StringComparison.Ordinal);
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

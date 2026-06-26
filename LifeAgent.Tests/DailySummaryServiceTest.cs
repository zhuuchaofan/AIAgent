using Xunit;
using System;
using LifeAgent.Api.Services;
using LifeAgent.Api.Models;

namespace LifeAgent.Tests;

public class DailySummaryServiceTest
{
    [Theory]
    [InlineData("2026-06-26", "Asia/Shanghai", "2026-06-25T16:00:00Z", "2026-06-26T16:00:00Z")]
    [InlineData("2026-06-26", "UTC", "2026-06-26T00:00:00Z", "2026-06-27T00:00:00Z")]
    public void GetUtcPeriod_ValidInputs_ReturnsCorrectUtcRange(string date, string timeZone, string expectedStartIso, string expectedEndIso)
    {
        // Act
        var (start, end) = DailySummaryService.GetUtcPeriod(date, timeZone);

        // Assert
        Assert.Equal(DateTime.Parse(expectedStartIso).ToUniversalTime(), start);
        Assert.Equal(DateTime.Parse(expectedEndIso).ToUniversalTime(), end);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
    }

    [Fact]
    public void GetUtcPeriod_InvalidDateFormat_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            DailySummaryService.GetUtcPeriod("2026/06/26", "Asia/Shanghai")
        );
    }

    [Fact]
    public void GetUtcPeriod_InvalidTimeZone_FallbackToUtc()
    {
        // Arrange
        string date = "2026-06-26";
        string invalidTimeZone = "Invalid/TimeZone_Name";

        // Act
        var (start, end) = DailySummaryService.GetUtcPeriod(date, invalidTimeZone);

        // Assert (should fallback to UTC)
        Assert.Equal(new DateTime(2026, 6, 26, 0, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 6, 27, 0, 0, 0, DateTimeKind.Utc), end);
    }

    [Fact]
    public void BuildEmptyDaySummary_CreatesCorrectModel()
    {
        // Arrange
        string userId = "test_user";
        string date = "2026-06-26";
        string timeZone = "Asia/Shanghai";
        var start = DateTime.UtcNow.AddDays(-1);
        var end = DateTime.UtcNow;
        string runId = "run_123";
        bool forceRegenerate = true;

        // Act
        var summary = DailySummaryService.BuildEmptyDaySummary(userId, date, timeZone, start, end, runId, forceRegenerate);

        // Assert
        Assert.Equal(date, summary.Id);
        Assert.Equal(userId, summary.UserId);
        Assert.Equal(date, summary.Date);
        Assert.Equal(timeZone, summary.TimeZone);
        Assert.Equal(start, summary.PeriodStartUtc);
        Assert.Equal(end, summary.PeriodEndUtc);
        Assert.Equal(0, summary.EventCount);
        Assert.Equal("这一天还没有记录。", summary.Summary);
        Assert.Empty(summary.Highlights);
        Assert.Equal("暂无记录", summary.MoodLabel);
        Assert.Null(summary.MoodScore);
        Assert.Empty(summary.Suggestions);
        Assert.Equal("empty_day", summary.GeneratedBy);
        Assert.Equal(runId, summary.AgentRunId);
        Assert.True(summary.ForceRegenerated);
    }
}

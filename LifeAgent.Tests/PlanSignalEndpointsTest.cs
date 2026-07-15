using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services.Plans;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAgent.Tests;

public class PlanSignalEndpointsTest
{
    [Fact]
    public async Task ConvertPlanSignalToReminder_ReturnsConvertedSignalAndReminder()
    {
        var userId = "user_a";
        var dueAt = new DateTime(2026, 7, 16, 9, 30, 0, DateTimeKind.Utc);
        var service = new ConvertingPlanSignalService(new PlanSignal
        {
            Id = "plan_1",
            UserId = userId,
            Kind = "reminder_signal",
            Title = "找键盘轴体",
            Content = "明天去公司前找一下键盘轴体",
            Status = "active",
            CreatedAt = DateTime.UtcNow.AddHours(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });

        var result = await ExecuteResultAsync(PlanSignalEndpoints.ConvertPlanSignalToReminderAsync(
            "plan_1",
            AuthenticatedContext(userId),
            service,
            new ConvertPlanSignalToReminderRequest
            {
                DueAt = dueAt,
                Timezone = "Asia/Shanghai"
            }));

        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.True(ReadBool(result.Body, "success"));
        Assert.Equal("converted", ReadString(result.Body, "data", "signal", "status"));
        Assert.Equal("pending", ReadString(result.Body, "data", "reminder", "status"));
        Assert.Equal("找键盘轴体", ReadString(result.Body, "data", "reminder", "title"));
        Assert.Equal(dueAt.ToString("O"), ReadString(result.Body, "data", "reminder", "dueAt"));
    }

    [Fact]
    public async Task ConvertPlanSignalToReminder_ReturnsNotFoundForMissingSignal()
    {
        var result = await ExecuteResultAsync(PlanSignalEndpoints.ConvertPlanSignalToReminderAsync(
            "missing",
            AuthenticatedContext("user_a"),
            new ConvertingPlanSignalService(null),
            new ConvertPlanSignalToReminderRequest
            {
                DueAt = DateTime.UtcNow.AddHours(1),
                Timezone = "Asia/Shanghai"
            }));

        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.False(ReadBool(result.Body, "success"));
    }

    private static DefaultHttpContext AuthenticatedContext(string userId)
    {
        var context = new DefaultHttpContext();
        context.Items["userId"] = userId;
        return context;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(Task<IResult> result)
    {
        return await ExecuteResultAsync(await result);
    }

    private static string ReadString(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        return ReadElement(document.RootElement, path).GetString() ?? string.Empty;
    }

    private static bool ReadBool(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        return ReadElement(document.RootElement, path).GetBoolean();
    }

    private static JsonElement ReadElement(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            current = current.GetProperty(segment);
        }

        return current;
    }

    private sealed class ConvertingPlanSignalService : IPlanSignalService
    {
        private readonly PlanSignal? _signal;

        public ConvertingPlanSignalService(PlanSignal? signal)
        {
            _signal = signal;
        }

        public Task<PlanSignal> CreateAsync(
            string userId,
            PlanSignal signal,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<PlanSignal>> ListAsync(
            string userId,
            string status = "active",
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PlanSignal?> GetAsync(
            string userId,
            string signalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_signal);
        }

        public Task<bool> ArchiveAsync(
            string userId,
            string signalId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<PlanSignalReminderConversionResult?> ConvertReminderSignalAsync(
            string userId,
            string signalId,
            PlanSignalReminderConversionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_signal is null)
            {
                return Task.FromResult<PlanSignalReminderConversionResult?>(null);
            }

            _signal.Status = "converted";
            _signal.ConvertedReminderId = "rem_1";
            _signal.ConvertedAt = DateTime.UtcNow;
            _signal.UpdatedAt = DateTime.UtcNow;

            return Task.FromResult<PlanSignalReminderConversionResult?>(new PlanSignalReminderConversionResult(
                _signal,
                new Reminder
                {
                    Id = "rem_1",
                    UserId = userId,
                    Title = _signal.Title,
                    Description = _signal.Content,
                    DueAt = request.DueAt,
                    Timezone = request.Timezone ?? "Asia/Shanghai",
                    Status = "pending"
                }));
        }
    }
}

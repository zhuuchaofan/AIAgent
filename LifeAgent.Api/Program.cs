using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using LifeAgent.Api.Middleware;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Firestore 初始化（注册为单例）────────────────────────────────
var firestoreProjectId = builder.Configuration["Firestore:ProjectId"]
    ?? Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID")
    ?? "copper-affinity-467409-k7";

builder.Services.AddSingleton(_ => FirestoreDb.Create(firestoreProjectId));

// ── 业务 Service 注册 ─────────────────────────────────────────
builder.Services.AddScoped<ILifeEventService, LifeEventService>();

var app = builder.Build();

// ── Firebase App 初始化（非 Mock 模式才需要）───────────────────
var useMockAuth = Environment.GetEnvironmentVariable("USE_MOCK_AUTH");
if (!string.Equals(useMockAuth, "true", StringComparison.OrdinalIgnoreCase))
{
    if (FirebaseApp.DefaultInstance == null)
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.GetApplicationDefault()
        });
        app.Logger.LogInformation("Firebase App 初始化完成（使用 ADC 凭证）");
    }
}

// ── 中间件注册 ─────────────────────────────────────────────────
app.UseMiddleware<FirebaseAuthMiddleware>();

// ── 路由 ──────────────────────────────────────────────────────

// GET /health — 无需鉴权
app.MapGet("/health", () => Results.Ok("healthy"));

// POST /debug/save-mock-event — 临时测试端点
// 用一个手写 Mock LifeEvent 验证 Firestore 写入链路是否通畅。
// 验证完成后可删除此端点。
app.MapPost("/debug/save-mock-event", async (
    HttpContext ctx,
    ILifeEventService svc) =>
{
    // 从 FirebaseAuthMiddleware 注入的 userId 中读取（Mock 模式下为 test_user_01）
    var userId = ctx.Items["userId"] as string;
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();

    // 手写 Mock 事件（模拟 LLM 解析后的结构化结果）
    var mockEvent = new LifeEvent
    {
        Type    = "cycling",
        Title   = "骑行 18km",
        Content = "今天骑车 18km，感觉腿有点酸但整体状态不错。",
        TimeZone = "Asia/Shanghai",
        Tags     = ["骑行", "健康", "运动"],
        Importance = 3,
        ExtractionConfidence = 0.95,
        NeedsReview = false,
        StructuredData = new()
        {
            ["distanceKm"]      = 18.0,
            ["durationMinutes"] = 60,
            ["avgHeartRate"]    = 145
        }
    };

    var saved = await svc.SaveEventAsync(userId, mockEvent);

    return Results.Ok(new
    {
        success = true,
        message = "Mock 事件已写入 Firestore",
        eventId = saved.Id,
        userId  = saved.UserId,
        path    = $"users/{saved.UserId}/life_events/{saved.Id}"
    });
});

app.Run();

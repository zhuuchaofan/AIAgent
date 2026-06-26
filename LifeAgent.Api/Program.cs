using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using LifeAgent.Api.Middleware;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Endpoints;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("LifeAgent.Tests")]

var builder = WebApplication.CreateBuilder(args);

// ── Firestore 初始化（注册为单例）────────────────────────────────
// Firestore 读写使用 GCP 计费项目
var firestoreProjectId = builder.Configuration["Firestore:ProjectId"]
    ?? Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID")
    ?? "copper-affinity-467409-k7";

// Firebase Auth 验签必须使用与前端 Firebase App 相同的 Firebase 项目
// 前端 NEXT_PUBLIC_FIREBASE_PROJECT_ID=my-agent-app-a5e42
var firebaseProjectId = builder.Configuration["Firebase:ProjectId"]
    ?? Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID")
    ?? "my-agent-app-a5e42";

builder.Services.AddSingleton(_ => FirestoreDb.Create(firestoreProjectId));

// ── 业务 Service 注册 ─────────────────────────────────────────
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.Rag));
builder.Services.AddScoped<ILifeEventService, LifeEventService>();
builder.Services.AddScoped<IReminderService, ReminderService>();
builder.Services.AddScoped<IDailySummaryService, DailySummaryService>();
builder.Services.AddHttpClient<IFirestoreVectorStore, RestFirestoreVectorStore>();
builder.Services.AddScoped<IRagChatService, RagChatService>();

builder.Services.AddSingleton<FileValidator>();
builder.Services.AddSingleton<ICloudStorageService, GoogleCloudStorageService>();
builder.Services.AddSingleton<IDocumentTextExtractor, PdfPigDocumentTextExtractor>();
builder.Services.AddSingleton<IChunker, BasicChunker>();
builder.Services.AddScoped<IDocumentRepository, FirestoreDocumentRepository>();
builder.Services.AddScoped<IChatSessionRepository, FirestoreChatSessionRepository>();
builder.Services.AddHttpClient<ICloudTasksService, CloudTasksService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<IEmbeddingService, MockEmbeddingService>();
    builder.Services.AddSingleton<IRagAnswerGenerator, MockRagAnswerGenerator>();
}
else
{
    builder.Services.AddHttpClient<IEmbeddingService, GeminiEmbeddingService>();
    builder.Services.AddHttpClient<IRagAnswerGenerator, GeminiRagAnswerGenerator>();
}

var useMockLlm = Environment.GetEnvironmentVariable("USE_MOCK_LLM");
if (string.Equals(useMockLlm, "true", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<ILlmService, MockLlmService>();
}
else
{
    builder.Services.AddHttpClient<ILlmService, GeminiLlmService>(client =>
    {
        // Gemini API 超时保护：真实调用通常 5-15s，上限设为 30s 防止前端无限 loading
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
var app = builder.Build();

// ── Firebase App 初始化（非 Mock 模式才需要）───────────────────
var useMockAuth = Environment.GetEnvironmentVariable("USE_MOCK_AUTH");
if (!string.Equals(useMockAuth, "true", StringComparison.OrdinalIgnoreCase))
{
    if (FirebaseApp.DefaultInstance == null)
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.GetApplicationDefault(),
            ProjectId = firebaseProjectId   // 必须与前端 Firebase Auth 项目一致
        });
        app.Logger.LogInformation("Firebase App 初始化完成（ProjectId: {ProjectId}）", firebaseProjectId);
    }
}

// ── 中间件注册 ─────────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<FirebaseAuthMiddleware>();

// ── 路由 ──────────────────────────────────────────────────────

app.MapLifeEndpoints();
app.MapReminderEndpoints();
app.MapDailySummaryEndpoints();
app.MapMigrationEndpoints();
app.MapDocumentEndpoints();
app.MapInternalDocumentEndpoints();
app.MapRagChatEndpoints();

// GET /health — 无需鉴权
app.MapGet("/health", () => Results.Ok("healthy"));

// 根目录访问
app.MapGet("/", () => Results.Ok("LifeAgent API is running. Please use /health to check status."));

if (app.Environment.IsDevelopment())
{
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

        // 手写 Mock 事件 A（骑行）
        var eventA = new LifeEvent
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

        // 手写 Mock 事件 B（猫）— 稍等 50ms 确保 occurredAt 时间戳不同
        await Task.Delay(50);
        var eventB = new LifeEvent
        {
            Type    = "cat",
            Title   = "喂猫并观察行为",
            Content = "今天猫咪喝了很多水，看起来很活跃。",
            TimeZone = "Asia/Shanghai",
            Tags     = ["猫", "健康"],
            Importance = 2,
            ExtractionConfidence = 0.88,
            NeedsReview = false,
            StructuredData = new()
            {
                ["catName"] = "小橘"
            }
        };

        var savedA = await svc.SaveEventAsync(userId, eventA);
        var savedB = await svc.SaveEventAsync(userId, eventB);

        return Results.Ok(new
        {
            success = true,
            message = "2 条 Mock 事件已写入 Firestore",
            events  = new[]
            {
                new { eventId = savedA.Id, type = savedA.Type, occurredAt = savedA.OccurredAt },
                new { eventId = savedB.Id, type = savedB.Type, occurredAt = savedB.OccurredAt }
            }
        });
    });

    // GET /debug/list-events — 临时分页查询测试端点
    // 验证 ListEventsAsync 的 cursor 分页、排序、路径正确性。
    app.MapGet("/debug/list-events", async (
        HttpContext ctx,
        ILifeEventService svc,
        string? type   = null,
        int     limit  = 20,
        string? cursor = null) =>
    {
        var userId = ctx.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
            return Results.Unauthorized();

        var result = await svc.ListEventsAsync(userId, type, limit, cursor);

        return Results.Ok(new
        {
            count      = result.Data.Count,
            nextCursor = result.NextCursor,
            // 解码 nextCursor 便于肉眼核查
            nextCursorDecoded = result.NextCursor is not null
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result.NextCursor))
                : null,
            data = result.Data.Select(e => new
            {
                e.Id,
                e.UserId,
                e.Type,
                e.Title,
                occurredAt = e.OccurredAt.ToString("O"),
                createdAt  = e.CreatedAt.ToString("O"),
                e.SchemaVersion,
                e.Source
            })
        });
    });
}


app.Run();

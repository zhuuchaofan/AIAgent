using System.Threading.RateLimiting;
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
builder.Services.AddSingleton<IDailyQuotaService, DailyQuotaService>();
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

// ── 请求体大小限制（防止超大文件绕过前端限制）────────────────
var maxRequestBodySizeMb = builder.Configuration.GetSection("Rag").GetValue<int>("MaxRequestBodySizeMb");
if (maxRequestBodySizeMb <= 0) maxRequestBodySizeMb = 15;
var maxRequestBodyBytes = (long)maxRequestBodySizeMb * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = maxRequestBodyBytes;
});

// ── 速率限制（Rate Limiting）─────────────────────────────────────
var rateLimitConfig = builder.Configuration
    .GetSection(RagOptions.Rag)
    .GetSection("RateLimiting")
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;

    // 自定义 429 响应格式，与 ExceptionMiddleware 风格一致
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            error = new
            {
                code = "RATE_LIMIT_EXCEEDED",
                message = "请求过于频繁，请稍后重试。"
            }
        }, cancellationToken);
    };

    // ── Partition key 提供器：优先 userId，降级 IP ──
    string GetPartitionKey(HttpContext ctx)
    {
        var userId = ctx.Items["userId"] as string;
        return !string.IsNullOrEmpty(userId) ? $"user:{userId}" : $"ip:{ctx.Connection.RemoteIpAddress}";
    }

    // Global IP 限流（未认证请求兜底）
    options.AddPolicy("global-ip", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitConfig.GlobalIp.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitConfig.GlobalIp.WindowSeconds),
                QueueLimit = rateLimitConfig.GlobalIp.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // 已认证用户限流
    options.AddPolicy("auth-user", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitConfig.AuthenticatedUser.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitConfig.AuthenticatedUser.WindowSeconds),
                QueueLimit = rateLimitConfig.AuthenticatedUser.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // 高成本端点限流
    options.AddPolicy("high-cost", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitConfig.HighCost.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitConfig.HighCost.WindowSeconds),
                QueueLimit = rateLimitConfig.HighCost.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));

    // Internal 端点限流（按 IP 分区，不依赖 Firebase Auth）
    options.AddPolicy("internal", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"internal:{httpContext.Connection.RemoteIpAddress}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitConfig.Internal.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitConfig.Internal.WindowSeconds),
                QueueLimit = rateLimitConfig.Internal.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            }));
});

var app = builder.Build();
app.Logger.LogInformation("MaxRequestBodySize set to {SizeMb} MB", maxRequestBodySizeMb);

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
app.UseRateLimiter();

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

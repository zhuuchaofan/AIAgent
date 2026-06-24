using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using LifeAgent.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// ── Firestore 初始化（注册为单例）────────────────────────────────
// GCP 项目 ID 优先读取环境变量 FIRESTORE_PROJECT_ID；
// 本地开发可在 launchSettings.json 或 shell export 中设置。
// Firestore SDK 会自动读取 GOOGLE_APPLICATION_CREDENTIALS 或 GCP ADC 凭证。
var firestoreProjectId = builder.Configuration["Firestore:ProjectId"]
    ?? Environment.GetEnvironmentVariable("FIRESTORE_PROJECT_ID")
    ?? "copper-affinity-467409-k7";

builder.Services.AddSingleton(_ => FirestoreDb.Create(firestoreProjectId));

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

// ── 中间件注册顺序 ───────────────────────────────────────────
app.UseMiddleware<FirebaseAuthMiddleware>();

// ── 路由 ────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

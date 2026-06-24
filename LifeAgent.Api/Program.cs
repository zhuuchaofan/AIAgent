using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using LifeAgent.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// ── Firebase App 初始化（非 Mock 模式才需要）───────────────────
// Mock 模式下直接跳过，不初始化 Firebase SDK
var useMockAuth = Environment.GetEnvironmentVariable("USE_MOCK_AUTH");
if (!string.Equals(useMockAuth, "true", StringComparison.OrdinalIgnoreCase))
{
    // 生产/测试环境：用 GOOGLE_APPLICATION_CREDENTIALS 或 GCP ADC 自动加载凭证
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
// FirebaseAuthMiddleware 放在所有业务路由之前
app.UseMiddleware<FirebaseAuthMiddleware>();

// ── 路由 ────────────────────────────────────────────────────
// GET /health 不需要鉴权（中间件内部已豁免）
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

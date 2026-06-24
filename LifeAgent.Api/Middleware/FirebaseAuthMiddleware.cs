using System.Net;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;

namespace LifeAgent.Api.Middleware;

public class FirebaseAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<FirebaseAuthMiddleware> _logger;

    public FirebaseAuthMiddleware(RequestDelegate next, ILogger<FirebaseAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // /health 不需要鉴权，直接放行
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // ── Mock 模式（本地开发提速）──────────────────────────────
        // 环境变量 USE_MOCK_AUTH=true 时跳过 Firebase 验签
        // 注入固定测试用户，无需任何 Token
        var useMockAuth = Environment.GetEnvironmentVariable("USE_MOCK_AUTH");
        if (string.Equals(useMockAuth, "true", StringComparison.OrdinalIgnoreCase))
        {
            context.Items["userId"] = "test_user_01";
            _logger.LogDebug("MockAuth 模式已启用，注入测试用户 test_user_01");
            await _next(context);
            return;
        }

        // ── 真实 Firebase 验签 ──────────────────────────────────
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorized(context, "UNAUTHORIZED", "缺少 Authorization: Bearer <token> 请求头");
            return;
        }

        var idToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(idToken))
        {
            await WriteUnauthorized(context, "UNAUTHORIZED", "Bearer Token 不能为空");
            return;
        }

        try
        {
            var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
            context.Items["userId"] = decoded.Uid;
            await _next(context);
        }
        catch (FirebaseAuthException ex)
        {
            _logger.LogWarning(ex, "Firebase Token 验签失败");
            await WriteUnauthorized(context, "UNAUTHORIZED", "Token 无效或已过期");
        }
    }

    // ── 统一 401 响应 ───────────────────────────────────────────
    private static async Task WriteUnauthorized(HttpContext context, string code, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = JsonSerializer.Serialize(new
        {
            success = false,
            error = new { code, message }
        });

        await context.Response.WriteAsync(body);
    }
}

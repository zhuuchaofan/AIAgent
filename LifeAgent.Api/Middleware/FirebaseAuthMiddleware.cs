using System.Net;
using System.Text.Json;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using LifeAgent.Api.Models.Exceptions;

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
        // /health 和 /internal 不需要 Firebase 鉴权，直接放行
        // /internal 端点使用 Cloud Tasks OIDC token，由端点自身进行验证
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/internal"))
        {
            await _next(context);
            return;
        }

        // ── Mock 模式（本地开发提速）──────────────────────────────
        // 环境变量 USE_MOCK_AUTH=true 时跳过 Firebase 验签
        // 注入固定测试用户，支持多用户和未登录测试
        var useMockAuth = Environment.GetEnvironmentVariable("USE_MOCK_AUTH");
        if (string.Equals(useMockAuth, "true", StringComparison.OrdinalIgnoreCase))
        {
            var mockAuthHeader = context.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(mockAuthHeader) || !mockAuthHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedException("缺少 Authorization: Bearer <token> 请求头");
            }

            var mockIdToken = mockAuthHeader["Bearer ".Length..].Trim();
            if (string.IsNullOrEmpty(mockIdToken))
            {
                throw new UnauthorizedException("Bearer Token 不能为空");
            }

            if (mockIdToken == "mock_local_token_123")
            {
                context.Items["userId"] = "test_user_01";
            }
            else if (mockIdToken == "mock_local_token_456")
            {
                context.Items["userId"] = "test_user_02";
            }
            else
            {
                context.Items["userId"] = $"mock_{mockIdToken}";
            }

            _logger.LogDebug("MockAuth 模式已启用，注入测试用户 {UserId}", context.Items["userId"]);
            await _next(context);
            return;
        }

        // ── 真实 Firebase 验签 ──────────────────────────────────
        var authHeader = context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorized(context, "缺少 Authorization: Bearer <token> 请求头");
            return;
        }

        var idToken = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(idToken))
        {
            await WriteUnauthorized(context, "Bearer Token 不能为空");
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
            _logger.LogWarning(ex, "Firebase ID Token 验签失败");
            throw new UnauthorizedException("无效或过期的 Token");
        }
    }

    private Task WriteUnauthorized(HttpContext context, string message)
    {
        throw new UnauthorizedException(message);
    }
}

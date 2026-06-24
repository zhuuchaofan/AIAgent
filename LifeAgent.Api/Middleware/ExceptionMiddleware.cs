using System.Text.Json;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Exceptions;

namespace LifeAgent.Api.Middleware;

/// <summary>
/// 全局异常处理中间件
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        string code = "INTERNAL_ERROR";
        int statusCode = StatusCodes.Status500InternalServerError;
        string message = "服务器内部错误";
        object? details = null;

        // 区分异常类型
        if (exception is LifeApiException apiEx)
        {
            // 业务预期内异常
            code = apiEx.Code;
            statusCode = apiEx.StatusCode;
            message = apiEx.Message;
            details = apiEx.Details;

            _logger.LogWarning(apiEx, "业务异常: {Code} - {Message}", code, message);
        }
        else
        {
            // 未处理的系统异常
            _logger.LogError(exception, "未处理系统异常");

            // 开发环境可以带上堆栈信息放在 details 里
            if (_env.IsDevelopment() || _env.EnvironmentName == "Debug")
            {
                details = new { stackTrace = exception.StackTrace };
                message = exception.Message;
            }
        }

        context.Response.StatusCode = statusCode;

        var response = new ErrorResponse
        {
            Success = false,
            Error = new ErrorDetail
            {
                Code = code,
                Message = message,
                Details = details
            }
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        return context.Response.WriteAsync(json);
    }
}

using System;
using System.Collections.Generic;

namespace LifeAgent.Api.Models.Exceptions;

/// <summary>
/// 业务异常基类。
/// 抛出此类异常时，ExceptionMiddleware 会捕获并返回对应状态码及错误信息。
/// </summary>
public class LifeApiException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }
    public object? Details { get; }

    public LifeApiException(string message, string code = "INTERNAL_ERROR", int statusCode = 500, object? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }
}

// 可定义具体异常便于抛出
public class InvalidInputException : LifeApiException
{
    public InvalidInputException(string message, object? details = null) 
        : base(message, "INVALID_INPUT", 400, details) { }
}

public class UnauthorizedException : LifeApiException
{
    public UnauthorizedException(string message = "未授权访问", object? details = null) 
        : base(message, "UNAUTHORIZED", 401, details) { }
}

public class EventNotFoundException : LifeApiException
{
    public EventNotFoundException(string id) 
        : base($"事件 {id} 不存在", "EVENT_NOT_FOUND", 404, new { id }) { }
}

public class LlmParseFailedException : LifeApiException
{
    public LlmParseFailedException(string message, object? details = null) 
        : base(message, "LLM_PARSE_FAILED", 422, details) { }
}

public class SchemaValidationFailedException : LifeApiException
{
    public SchemaValidationFailedException(string message, object? details = null) 
        : base(message, "SCHEMA_VALIDATION_FAILED", 422, details) { }
}

public class ReminderNotFoundException : LifeApiException
{
    public ReminderNotFoundException(string id)
        : base($"提醒 {id} 不存在", "REMINDER_NOT_FOUND", 404, new { id }) { }
}

public class QuotaExceededException : LifeApiException
{
    public QuotaExceededException(string quotaType, int remaining = 0)
        : base($"今日 {quotaType} 调用次数已达上限，请明天再试。",
               "QUOTA_EXCEEDED",
               429,
               new { quotaType, remaining }) { }
}

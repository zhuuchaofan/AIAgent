using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using LifeAgent.Api.Models;
using LifeAgent.Api.Services;
using LifeAgent.Api.Models.Exceptions;
using static LifeAgent.Api.Services.DailyQuotaService;

namespace LifeAgent.Api.Endpoints;

public static class RagChatEndpoints
{
    public static void MapRagChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/chat/rag", ProcessRagChatAsync);
        app.MapGet("/api/v1/chat/rag/{conversationId}/messages", GetRagChatHistoryAsync);
        app.MapDelete("/api/v1/chat/rag/{conversationId}/messages", ClearRagChatHistoryAsync);
    }

    public static async Task<IResult> ProcessRagChatAsync(
        HttpContext httpContext,
        [FromBody] RagChatRequest request,
        [FromServices] IRagChatService ragChatService,
        [FromServices] IDailyQuotaService quotaService,
        [FromServices] ILogger<RagChatService> logger)
    {
        var userId = httpContext.Items["userId"] as string;
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        if (request == null || string.IsNullOrEmpty(request.ConversationId) || string.IsNullOrEmpty(request.Message))
        {
            return Results.BadRequest(new { success = false, message = "Required parameters (conversationId, message) are missing." });
        }

        // RAG 问答同时消耗 1 次 Embedding + 1 次 LLM，需双重配额检查
        if (!quotaService.CheckAndIncrement(userId, QuotaTypeEmbedding))
        {
            return Results.Json(new { success = false, message = "今日 AI 调用次数已达上限，请明天再试。" }, statusCode: 429);
        }
        if (!quotaService.CheckAndIncrement(userId, QuotaTypeLlm))
        {
            return Results.Json(new { success = false, message = "今日 AI 调用次数已达上限，请明天再试。" }, statusCode: 429);
        }

        logger.LogInformation("[RAG Endpoint] POST ProcessRagChatAsync. User: {User}... | Session: {Session} | MessageLen: {MsgLen}",
            desensitizedUserId, request.ConversationId, request.Message.Length);

        try
        {
            var response = await ragChatService.ProcessChatAsync(userId, request);
            return Results.Ok(response);
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "Session not found or ownership mismatch for User: {UserId}, Session: {SessionId}", userId, request.ConversationId);
            return Results.Json(new { success = false, message = "Conversation not found or access denied." }, statusCode: 404);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { success = false, message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Embedding dimension anomaly or execution logic exception for User: {UserId}", userId);
            return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error in RAG chat endpoint for User: {UserId}", userId);
            return Results.Json(new { success = false, message = "An internal server error occurred." }, statusCode: 500);
        }
    }

    public static async Task<IResult> GetRagChatHistoryAsync(
        string conversationId,
        HttpContext httpContext,
        [FromServices] IChatSessionRepository sessionRepository,
        [FromServices] ILogger<RagChatService> logger)
    {
        var userId = httpContext.Items["userId"] as string;
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        if (string.IsNullOrEmpty(conversationId))
        {
            return Results.BadRequest(new { success = false, message = "Required parameter (conversationId) is missing." });
        }

        logger.LogInformation("[RAG Endpoint] GET GetRagChatHistoryAsync. User: {User}... | Session: {Session}",
            desensitizedUserId, conversationId);

        try
        {
            // Verify session existence to validate ownership/access
            var session = await sessionRepository.GetSessionAsync(userId, conversationId);
            if (session == null)
            {
                return Results.Json(new { success = false, message = "Conversation not found or access denied." }, statusCode: 404);
            }

            // Retrieve recent messages (up to 50 for history display)
            var messages = await sessionRepository.GetRecentMessagesAsync(userId, conversationId, 50);
            return Results.Ok(new { success = true, data = messages });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error retrieving chat history for User: {UserId}, Session: {SessionId}", userId, conversationId);
            return Results.Json(new { success = false, message = "An internal server error occurred." }, statusCode: 500);
        }
    }

    public static async Task<IResult> ClearRagChatHistoryAsync(
        string conversationId,
        HttpContext httpContext,
        [FromServices] IChatSessionRepository sessionRepository,
        [FromServices] ILogger<RagChatService> logger)
    {
        var userId = httpContext.Items["userId"] as string;
        var desensitizedUserId = string.IsNullOrEmpty(userId) ? "" : (userId.Length > 6 ? userId.Substring(0, 6) : userId);
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        if (string.IsNullOrEmpty(conversationId))
        {
            return Results.BadRequest(new { success = false, message = "Required parameter (conversationId) is missing." });
        }

        logger.LogInformation("[RAG Endpoint] DELETE ClearRagChatHistoryAsync. User: {User}... | Session: {Session}",
            desensitizedUserId, conversationId);

        try
        {
            // Verify session exists and belongs to user
            var session = await sessionRepository.GetSessionAsync(userId, conversationId);
            if (session == null)
            {
                return Results.Json(new { success = false, message = "Conversation not found or access denied." }, statusCode: 404);
            }

            await sessionRepository.DeleteAllMessagesAsync(userId, conversationId);

            logger.LogInformation("[RAG Endpoint] Successfully cleared messages for Session: {Session}", conversationId);
            return Results.Ok(new { success = true, message = "Conversation cleared successfully." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error clearing chat history for User: {UserId}, Session: {SessionId}", userId, conversationId);
            return Results.Json(new { success = false, message = "An internal server error occurred." }, statusCode: 500);
        }
    }
}

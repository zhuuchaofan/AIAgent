using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services.Agent;
using LifeAgent.Api.Services.LifeEvents;
using Microsoft.AspNetCore.Mvc;

namespace LifeAgent.Api.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        app.MapPost("/api/agent/run", RunAgentPreviewAsync)
            .WithTags("agent")
            .RequireRateLimiting("high-cost");

        app.MapPost("/api/agent/confirm", ConfirmAgentActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("high-cost");
    }

    public static async Task<IResult> RunAgentPreviewAsync(
        HttpContext httpContext,
        [FromBody] AgentRunRequest request,
        [FromServices] AgentRunner agentRunner,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        request ??= new AgentRunRequest();
        var response = await agentRunner.RunAsync(userId, request, cancellationToken);
        return Results.Ok(new AgentRunApiResponse
        {
            Success = true,
            Data = response
        });
    }

    public static Task<IResult> ConfirmAgentActionAsync(
        HttpContext httpContext,
        [FromBody] AgentConfirmationRequest request,
        [FromServices] IPendingAgentActionStore pendingActions,
        [FromServices] IAgentWriteFeatureGate agentWriteFeatureGate,
        [FromServices] AgentLifeEventConfirmationWriteCoordinator lifeEventWriteCoordinator,
        [FromServices] ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult<IResult>(Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401));
        }

        request ??= new AgentConfirmationRequest();
        return ConfirmAsync();

        async Task<IResult> ConfirmAsync()
        {
            var logger = loggerFactory.CreateLogger("AgentEndpoints");
            var actionId = request.ActionId ?? string.Empty;
            var normalizedDecision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
            logger.LogInformation(
                "Agent confirm request received. UserId={UserId}, ActionId={ActionId}, Decision={Decision}",
                userId, actionId, normalizedDecision);

            if (normalizedDecision == "confirm")
            {
                var pending = await pendingActions.GetAsync(userId, actionId, cancellationToken);
                var canCreateLifeEvent = agentWriteFeatureGate.CanCreateLifeEvent();
                logger.LogInformation(
                    "Agent confirm action lookup completed. UserId={UserId}, ActionId={ActionId}, ActionFound={ActionFound}, ActionType={ActionType}, LifecycleStatus={LifecycleStatus}, FeatureGateCanCreateLifeEvent={FeatureGateCanCreateLifeEvent}",
                    userId,
                    actionId,
                    pending is not null,
                    pending?.ProposedAction.ActionType,
                    pending?.Status,
                    canCreateLifeEvent);

                if (IsCreateLifeEventWriteAction(pending?.ProposedAction.ActionType) &&
                    canCreateLifeEvent)
                {
                    logger.LogInformation(
                        "Agent confirm entering real write branch. UserId={UserId}, ActionId={ActionId}, ActionType={ActionType}, FeatureGateCanCreateLifeEvent={FeatureGateCanCreateLifeEvent}",
                        userId, actionId, pending!.ProposedAction.ActionType, canCreateLifeEvent);
                    var writeResponse = await lifeEventWriteCoordinator.ConfirmCreateLifeEventAsync(userId, actionId, cancellationToken);
                    LogConfirmResponse(logger, "Agent confirm real write branch completed.", userId, writeResponse);
                    return Results.Ok(writeResponse);
                }

                var validation = ValidateCreateLifeEventPreviewOnlyPath(pending);
                if (validation is not null)
                {
                    LogConfirmResponse(logger, "Agent confirm preview validation failed.", userId, validation);
                    return Results.Ok(validation);
                }
            }

            var response = await pendingActions.ConfirmAsync(userId, actionId, normalizedDecision, cancellationToken);
            LogConfirmResponse(logger, "Agent confirm preview-only branch completed.", userId, response);
            return Results.Ok(response);
        }
    }

    private static void LogConfirmResponse(
        ILogger logger,
        string message,
        string userId,
        AgentConfirmationResponse response)
    {
        var result = System.Text.Json.JsonSerializer.SerializeToElement(response.Result ?? new { });
        var previewOnly = ReadBool(result, "previewOnly");
        var wroteData = ReadBool(result, "wroteData");
        var createdResourceType = ReadString(result, "createdResourceType");
        var createdResourceId = ReadString(result, "createdResourceId");
        var idempotent = ReadBool(result, "idempotent");

        logger.LogInformation(
            "{Message} UserId={UserId}, ActionId={ActionId}, ActionType={ActionType}, Success={Success}, ErrorCode={ErrorCode}, LifecycleStatus={LifecycleStatus}, PreviewOnly={PreviewOnly}, WroteData={WroteData}, CreatedResourceType={CreatedResourceType}, CreatedResourceId={CreatedResourceId}, Idempotent={Idempotent}",
            message,
            userId,
            response.ActionId,
            response.ActionType,
            response.Success,
            response.Status,
            response.LifecycleStatus,
            previewOnly,
            wroteData,
            createdResourceType,
            createdResourceId,
            idempotent);
    }

    private static bool? ReadBool(System.Text.Json.JsonElement element, string propertyName)
    {
        return element.ValueKind == System.Text.Json.JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static string? ReadString(System.Text.Json.JsonElement element, string propertyName)
    {
        return element.ValueKind == System.Text.Json.JsonValueKind.Object &&
               element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == System.Text.Json.JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static AgentConfirmationResponse? ValidateCreateLifeEventPreviewOnlyPath(
        PendingAgentAction? pending)
    {
        if (pending is null || !IsCreateLifeEventAction(pending.ProposedAction.ActionType))
        {
            return null;
        }

        if (!LooksLikeCreateLifeEventPayload(pending.ProposedAction.Payload))
        {
            return null;
        }

        try
        {
            _ = LifeEventActionPayloadMapper.Map(pending.ProposedAction.Payload);
        }
        catch (ArgumentException ex)
        {
            return new AgentConfirmationResponse
            {
                Success = false,
                Status = "invalid_payload",
                Message = ex.Message,
                ActionId = pending.ProposedAction.ActionId,
                ActionType = pending.ProposedAction.ActionType,
                LifecycleStatus = pending.Status,
                Result = new
                {
                    previewOnly = true,
                    wroteData = false,
                    actionType = pending.ProposedAction.ActionType
                }
            };
        }

        return null;
    }

    private static bool IsCreateLifeEventWriteAction(string? actionType)
    {
        return string.Equals(actionType, "create_life_event", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCreateLifeEventAction(string? actionType)
    {
        return string.Equals(actionType, "create_life_event", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(actionType, "create_life_event_preview", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCreateLifeEventPayload(object? payload)
    {
        if (payload is null)
        {
            return false;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return false;
        }

        return document.RootElement.TryGetProperty("type", out _) ||
               document.RootElement.TryGetProperty("title", out _) ||
               document.RootElement.TryGetProperty("content", out _) ||
               document.RootElement.TryGetProperty("structuredData", out _);
    }
}

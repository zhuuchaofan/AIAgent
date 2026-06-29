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
            var actionId = request.ActionId ?? string.Empty;
            var normalizedDecision = (request.Decision ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedDecision == "confirm")
            {
                var pending = await pendingActions.GetAsync(userId, actionId, cancellationToken);
                if (IsCreateLifeEventWriteAction(pending?.ProposedAction.ActionType) &&
                    agentWriteFeatureGate.CanCreateLifeEvent())
                {
                    var writeResponse = await lifeEventWriteCoordinator.ConfirmCreateLifeEventAsync(userId, actionId, cancellationToken);
                    return Results.Ok(writeResponse);
                }

                var validation = ValidateCreateLifeEventPreviewOnlyPath(pending);
                if (validation is not null)
                {
                    return Results.Ok(validation);
                }
            }

            var response = await pendingActions.ConfirmAsync(userId, actionId, normalizedDecision, cancellationToken);
            return Results.Ok(response);
        }
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

using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services.Agent;
using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.Agent.Phase8;
using LifeAgent.Api.Services.LifeEvents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LifeAgent.Api.Endpoints;

public static class AgentEndpoints
{
    public static void MapAgentEndpoints(this WebApplication app)
    {
        // Legacy technical Agent/RAG preview route. LifeOS Personal Home uses
        // /api/agent/pending-actions as its only pending action mainline.
        app.MapPost("/api/agent/run", RunAgentPreviewAsync)
            .WithTags("agent")
            .RequireRateLimiting("high-cost");

        // Legacy confirmation route for PendingAgentAction. It is retained for
        // old Agent Preview tests and remains guarded by the real-write flags.
        // LifeOS Personal Home must not call this endpoint.
        app.MapPost("/api/agent/confirm", ConfirmAgentActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("high-cost");

        // Personal Agent v2 pending action endpoints are deliberately separate
        // from the legacy /api/agent/confirm path. They never call
        // IPendingAgentActionStore, FirestorePendingAgentActionStore, or the
        // life_event write coordinator.
        app.MapPost("/api/agent/pending-actions", CreatePhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapGet("/api/agent/pending-actions", ListPhase80PendingActionsAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapPost("/api/agent/pending-actions/{actionId}/confirm", ConfirmPhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapPost("/api/agent/pending-actions/{actionId}/cancel", CancelPhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapPost("/api/agent/pending-actions/{actionId}/archive", ArchivePhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        // Compatibility aliases for the Phase 8 preview deployment.
        // These routes hit the same Personal Agent v2 runtime and store.
        // They remain separate from the
        // legacy /api/agent/confirm path. They never call IPendingAgentActionStore,
        // FirestorePendingAgentActionStore, or the life_event write coordinator.
        app.MapPost("/api/agent/pending-actions/demo", CreatePhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapGet("/api/agent/pending-actions/demo", ListPhase80PendingActionsAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapPost("/api/agent/pending-actions/demo/{actionId}/confirm", ConfirmPhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapPost("/api/agent/pending-actions/demo/{actionId}/cancel", CancelPhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");

        app.MapPost("/api/agent/pending-actions/demo/{actionId}/archive", ArchivePhase80PendingActionAsync)
            .WithTags("agent")
            .RequireRateLimiting("auth-user");
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

    public static async Task<IResult> CreatePhase80PendingActionAsync(
        HttpContext httpContext,
        [FromBody] Phase80CreatePendingActionRequest? request,
        CancellationToken cancellationToken = default)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        var runtime = httpContext.RequestServices.GetRequiredService<Phase80PendingActionRuntime>();
        var result = await runtime.CreateAsync(userId, request, cancellationToken);
        return ToPhase80HttpResult(result);
    }

    public static async Task<IResult> ListPhase80PendingActionsAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        var runtime = httpContext.RequestServices.GetRequiredService<Phase80PendingActionRuntime>();
        var options = httpContext.RequestServices
            .GetRequiredService<IOptions<PendingActionPersistenceOptions>>()
            .Value;

        return Results.Ok(new
        {
            success = true,
            data = await runtime.ListAsync(userId, cancellationToken),
            persistence = new
            {
                storeMode = options.Mode,
                firestorePersistenceEnabled = options.UseFirestore,
                previewOnly = options.PreviewOnly,
                safetyMode = options.SafetyMode
            }
        });
    }

    public static async Task<IResult> ConfirmPhase80PendingActionAsync(
        HttpContext httpContext,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        return ToPhase80HttpResult(await httpContext.RequestServices
            .GetRequiredService<Phase80PendingActionRuntime>()
            .ConfirmAsync(userId, actionId, cancellationToken));
    }

    public static async Task<IResult> CancelPhase80PendingActionAsync(
        HttpContext httpContext,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        return ToPhase80HttpResult(await httpContext.RequestServices
            .GetRequiredService<Phase80PendingActionRuntime>()
            .CancelAsync(userId, actionId, cancellationToken));
    }

    public static async Task<IResult> ArchivePhase80PendingActionAsync(
        HttpContext httpContext,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401);
        }

        return ToPhase80HttpResult(await httpContext.RequestServices
            .GetRequiredService<Phase80PendingActionRuntime>()
            .ArchiveAsync(userId, actionId, cancellationToken));
    }

    private static IResult ToPhase80HttpResult(Phase80PendingActionResult result)
    {
        if (result.Success)
        {
            return Results.Ok(result);
        }

        var statusCode = result.Status switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            Phase80PendingActionRuntime.Confirmed => StatusCodes.Status409Conflict,
            Phase80PendingActionRuntime.Cancelled => StatusCodes.Status409Conflict,
            Phase80PendingActionRuntime.Expired => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(result, statusCode: statusCode);
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
            // Legacy Agent Preview confirmation path. This can enter the
            // create_life_event write coordinator only when both real-write
            // feature flags are explicitly enabled. Phase 8 demo confirmations
            // must use /api/agent/pending-actions/{actionId}/confirm.
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
                    var contractValidation = new AgentContractValidator().ValidatePendingConfirmation(pending);
                    if (!contractValidation.Success)
                    {
                        var validationError = BuildContractValidationResponse(pending, contractValidation.ErrorMessage);
                        LogConfirmResponse(logger, "Agent confirm contract validation failed.", userId, validationError);
                        return Results.Ok(validationError);
                    }

                    logger.LogInformation(
                        "Agent confirm entering real write branch. UserId={UserId}, ActionId={ActionId}, ActionType={ActionType}, FeatureGateCanCreateLifeEvent={FeatureGateCanCreateLifeEvent}",
                        userId, actionId, pending!.ProposedAction.ActionType, canCreateLifeEvent);
                    var writeResponse = await lifeEventWriteCoordinator.ConfirmCreateLifeEventAsync(userId, actionId, cancellationToken);
                    LogConfirmResponse(logger, "Agent confirm real write branch completed.", userId, writeResponse);
                    return Results.Ok(writeResponse);
                }

                var validation = ValidatePendingPreviewOnlyPath(pending);
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

    private static AgentConfirmationResponse? ValidatePendingPreviewOnlyPath(
        PendingAgentAction? pending)
    {
        var contractValidation = new AgentContractValidator().ValidatePendingConfirmation(pending);
        if (!contractValidation.Success)
        {
            return BuildContractValidationResponse(pending, contractValidation.ErrorMessage);
        }

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
                    actionType = pending.ProposedAction.ActionType,
                    createdResourceId = (string?)null
                }
            };
        }

        return null;
    }

    private static AgentConfirmationResponse BuildContractValidationResponse(
        PendingAgentAction? pending,
        string? errorMessage)
    {
        return new AgentConfirmationResponse
        {
            Success = false,
            Status = "invalid_payload",
            Message = errorMessage ?? "Agent confirmation contract validation failed.",
            ActionId = pending?.ProposedAction.ActionId,
            ActionType = pending?.ProposedAction.ActionType,
            LifecycleStatus = pending?.Status,
            Result = new
            {
                previewOnly = true,
                wroteData = false,
                actionType = pending?.ProposedAction.ActionType,
                createdResourceId = (string?)null
            }
        };
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

using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services.Agent;
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
        [FromServices] IPendingAgentActionStore pendingActions)
    {
        var userId = httpContext.Items["userId"] as string;
        if (string.IsNullOrEmpty(userId))
        {
            return Task.FromResult<IResult>(Results.Json(new { success = false, message = "Unauthorized: User ID is missing from security context." }, statusCode: 401));
        }

        request ??= new AgentConfirmationRequest();
        var response = pendingActions.Confirm(userId, request.ActionId, request.Decision);
        return Task.FromResult<IResult>(Results.Ok(response));
    }
}

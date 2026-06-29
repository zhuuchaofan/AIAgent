using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Agent;
using LifeAgent.Api.Services.Agent.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LifeAgent.Tests;

public class AgentSkeletonTest
{
    [Fact]
    public void ToolRegistry_RegistersAndFindsToolsByName()
    {
        var registry = new ToolRegistry(new IAgentTool[]
        {
            new StubAgentTool("list_documents"),
            new StubAgentTool("get_document_status")
        });

        Assert.True(registry.TryGet("list_documents", out var tool));
        Assert.NotNull(tool);
        Assert.Equal("list_documents", tool.Name);
        Assert.True(registry.TryGet("GET_DOCUMENT_STATUS", out var caseInsensitiveTool));
        Assert.Equal("get_document_status", caseInsensitiveTool!.Name);
        Assert.False(registry.TryGet("missing_tool", out _));
    }

    [Fact]
    public async Task ToolExecutor_RejectsUnknownTool()
    {
        var registry = new ToolRegistry(Array.Empty<IAgentTool>());
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);
        var context = new AgentContext { UserId = "user_a", RunId = "run_1" };

        var result = await executor.ExecuteAsync(
            context,
            "unknown_tool",
            JsonSerializer.SerializeToElement(new { }),
            1,
            CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("Unknown agent tool", result.ErrorMessage);
    }

    [Fact]
    public async Task AgentRunner_ClampsMaxIterationsToFive()
    {
        var registry = new ToolRegistry(Array.Empty<IAgentTool>());
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);
        var runner = new AgentRunner(
            executor,
            Options.Create(new AgentOptions { MaxIterations = 99 }));

        var response = await runner.RunAsync("user_a", new AgentRunRequest(), CancellationToken.None);

        Assert.Equal("completed", response.Status);
        Assert.Equal(5, response.MaxIterations);
        Assert.Empty(response.ToolCalls);
    }

    [Fact]
    public async Task ListDocumentsTool_OnlyReadsDocumentsForCurrentUser()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 }
        };
        repository.Documents["user_b"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_b", UserId = "user_b", FileName = "b.md", Status = "success", ChunkCount = 1 }
        };

        var tool = new ListDocumentsTool(repository);
        var result = await tool.ExecuteAsync(
            new AgentContext { UserId = "user_a" },
            JsonSerializer.SerializeToElement(new { status = "all" }),
            CancellationToken.None);

        var json = JsonSerializer.Serialize(result.Output);
        Assert.True(result.Success);
        Assert.Contains("doc_a", json);
        Assert.DoesNotContain("doc_b", json);
        Assert.Equal("user_a", repository.LastListUserId);
    }

    [Fact]
    public async Task GetDocumentStatusTool_ReturnsNotFoundForOtherUsersDocument()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_b"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_b", UserId = "user_b", FileName = "b.md", Status = "success", ChunkCount = 1 }
        };

        var tool = new GetDocumentStatusTool(repository);
        var result = await tool.ExecuteAsync(
            new AgentContext { UserId = "user_a" },
            JsonSerializer.SerializeToElement(new { documentId = "doc_b" }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("user_a", repository.LastGetUserId);
    }

    [Fact]
    public async Task AgentRunPreviewEndpoint_ReturnsMockResultWithoutToolName()
    {
        var registry = new ToolRegistry(Array.Empty<IAgentTool>());
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);
        var runner = new AgentRunner(executor, Options.Create(new AgentOptions { MaxIterations = 3 }));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.RunAgentPreviewAsync(
            context,
            new AgentRunRequest { Message = "hello" },
            runner,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentRunResponse>>(result);
        Assert.Equal("completed", ok.Value!.Status);
        Assert.Contains("preview skeleton", ok.Value.Message);
        Assert.Empty(ok.Value.ToolCalls);
    }

    private sealed class StubAgentTool : IAgentTool
    {
        public StubAgentTool(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public string Description => "stub";
        public AgentToolRisk Risk => AgentToolRisk.Read;
        public bool RequiresConfirmation => false;

        public Task<AgentToolResult> ExecuteAsync(AgentContext context, JsonElement input, CancellationToken cancellationToken)
        {
            return Task.FromResult(AgentToolResult.Ok(new { ok = true }));
        }
    }

    private sealed class FakeDocumentRepository : IDocumentRepository
    {
        public Dictionary<string, List<KnowledgeDocument>> Documents { get; } = new();
        public string? LastListUserId { get; private set; }
        public string? LastGetUserId { get; private set; }

        public Task CreateAsync(KnowledgeDocument doc)
        {
            throw new NotSupportedException();
        }

        public Task<KnowledgeDocument?> GetAsync(string userId, string documentId)
        {
            LastGetUserId = userId;
            Documents.TryGetValue(userId, out var docs);
            return Task.FromResult(docs?.FirstOrDefault(doc => doc.Id == documentId));
        }

        public Task<List<KnowledgeDocument>> ListAsync(string userId, int limit, string? cursor)
        {
            LastListUserId = userId;
            Documents.TryGetValue(userId, out var docs);
            return Task.FromResult((docs ?? new List<KnowledgeDocument>()).Take(limit).ToList());
        }

        public Task UpdateAsync(KnowledgeDocument doc)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(string userId, string documentId)
        {
            throw new NotSupportedException();
        }
    }
}

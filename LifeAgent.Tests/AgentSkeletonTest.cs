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

        Assert.Equal("preview_readonly", response.Mode);
        Assert.Equal(5, response.MaxSteps);
        Assert.Equal(0, response.StepsUsed);
        Assert.Empty(response.ToolCalls);
    }

    [Fact]
    public async Task AgentRunner_ListDocumentsIntent_CallsListDocumentsTool()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 },
            new KnowledgeDocument { Id = "doc_b", UserId = "user_a", FileName = "b.md", Status = "processing", ChunkCount = 0 }
        };
        var runner = CreateRunner(repository);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "请列出文档" },
            CancellationToken.None);

        Assert.Equal("preview_readonly", response.Mode);
        Assert.Equal(1, response.StepsUsed);
        Assert.Single(response.ToolCalls);
        Assert.Equal("list_documents", response.ToolCalls[0].ToolName);
        Assert.Equal("success", response.ToolCalls[0].Status);
        Assert.Equal("2 documents, 1 success", response.ToolCalls[0].OutputSummary);
        Assert.Equal("user_a", repository.LastListUserId);
    }

    [Fact]
    public async Task AgentRunner_DocumentStatusIntent_CallsGetDocumentStatusTool()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "processing", ChunkCount = 0 }
        };
        var runner = CreateRunner(repository);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "查询 doc_a 文档状态" },
            CancellationToken.None);

        Assert.Equal(1, response.StepsUsed);
        Assert.Equal("get_document_status", response.ToolCalls[0].ToolName);
        Assert.Equal("success", response.ToolCalls[0].Status);
        Assert.Equal("doc_a status processing", response.ToolCalls[0].OutputSummary);
        Assert.Equal("user_a", repository.LastGetUserId);
    }

    [Fact]
    public async Task AgentRunner_UnknownIntent_DoesNotCallWriteTools()
    {
        var writeTool = new StubAgentTool("create_life_event", AgentToolRisk.Write, requiresConfirmation: true);
        var registry = new ToolRegistry(new IAgentTool[] { writeTool });
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);
        var runner = new AgentRunner(executor, Options.Create(new AgentOptions { MaxIterations = 3 }));

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存一条记忆" },
            CancellationToken.None);

        Assert.Equal(0, response.StepsUsed);
        Assert.Empty(response.ToolCalls);
        Assert.Contains("只支持文档列表与文档状态查询", response.Answer);
        Assert.False(writeTool.WasCalled);
    }

    [Fact]
    public async Task AgentRunner_ExplicitUnknownToolName_FailsSafely()
    {
        var runner = CreateRunner(new FakeDocumentRepository());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { ToolName = "unknown_tool" },
            CancellationToken.None);

        Assert.Equal(1, response.StepsUsed);
        Assert.Equal("failed", response.ToolCalls[0].Status);
        Assert.Contains("Unknown agent tool", response.ToolCalls[0].ErrorMessage);
    }

    [Fact]
    public async Task AgentRunner_UsesAuthenticatedUserIdAndIgnoresRequestUserId()
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
        var runner = CreateRunner(repository);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest
            {
                Message = "列出文档",
                ToolInput = JsonSerializer.SerializeToElement(new { userId = "user_b" })
            },
            CancellationToken.None);

        var json = JsonSerializer.Serialize(response.ToolCalls[0].Output);
        Assert.Equal("user_a", repository.LastListUserId);
        Assert.Contains("doc_a", json);
        Assert.DoesNotContain("doc_b", json);
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

        var ok = Assert.IsType<Ok<AgentRunApiResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal("preview_readonly", ok.Value.Data.Mode);
        Assert.Contains("只支持文档列表与文档状态查询", ok.Value.Data.Answer);
        Assert.Empty(ok.Value.Data.ToolCalls);
    }

    private sealed class StubAgentTool : IAgentTool
    {
        public StubAgentTool(string name, AgentToolRisk risk = AgentToolRisk.Read, bool requiresConfirmation = false)
        {
            Name = name;
            Risk = risk;
            RequiresConfirmation = requiresConfirmation;
        }

        public string Name { get; }
        public string Description => "stub";
        public AgentToolRisk Risk { get; }
        public bool RequiresConfirmation { get; }
        public bool WasCalled { get; private set; }

        public Task<AgentToolResult> ExecuteAsync(AgentContext context, JsonElement input, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(AgentToolResult.Ok(new { ok = true }));
        }
    }

    private static AgentRunner CreateRunner(IDocumentRepository repository, int maxIterations = 3)
    {
        var tools = new IAgentTool[]
        {
            new ListDocumentsTool(repository),
            new GetDocumentStatusTool(repository),
            new SearchDocumentsTool(),
            new AnswerWithRagTool()
        };
        var registry = new ToolRegistry(tools);
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);
        return new AgentRunner(executor, Options.Create(new AgentOptions { MaxIterations = maxIterations }));
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

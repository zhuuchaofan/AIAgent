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
            Options.Create(new AgentOptions { MaxIterations = 99 }),
            new InMemoryPendingAgentActionStore());

        var response = await runner.RunAsync("user_a", new AgentRunRequest(), CancellationToken.None);

        Assert.Equal("preview_readonly_rag", response.Mode);
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

        Assert.Equal("preview_readonly_rag", response.Mode);
        Assert.Equal(1, response.StepsUsed);
        Assert.Single(response.ToolCalls);
        Assert.Equal("list_documents", response.ToolCalls[0].ToolName);
        Assert.Equal("success", response.ToolCalls[0].Status);
        Assert.Equal("2 documents, 1 success", response.ToolCalls[0].OutputSummary);
        Assert.Equal("user_a", repository.LastListUserId);
    }

    [Theory]
    [InlineData("列出我的文档")]
    [InlineData("有哪些文档")]
    [InlineData("文档列表")]
    public async Task AgentRunner_ChineseListDocumentsIntents_CallListDocumentsTool(string message)
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 }
        };
        var runner = CreateRunner(repository);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = message },
            CancellationToken.None);

        Assert.Equal(1, response.StepsUsed);
        Assert.Single(response.ToolCalls);
        Assert.Equal("list_documents", response.ToolCalls[0].ToolName);
        Assert.Equal("success", response.ToolCalls[0].Status);
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
        var runner = new AgentRunner(executor, Options.Create(new AgentOptions { MaxIterations = 3 }), new InMemoryPendingAgentActionStore());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存一条记忆" },
            CancellationToken.None);

        Assert.Equal(0, response.StepsUsed);
        Assert.Empty(response.ToolCalls);
        Assert.Contains("支持文档列表、文档状态查询和只读 RAG 问答", response.Answer);
        Assert.False(writeTool.WasCalled);
    }

    [Fact]
    public async Task AgentRunner_WritePreviewIntent_ReturnsProposedAction()
    {
        var store = new InMemoryPendingAgentActionStore();
        var runner = CreateRunner(new FakeDocumentRepository(), pendingActions: store);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我记一下：今天黑猫吐了一次" },
            CancellationToken.None);

        Assert.True(response.RequiresConfirmation);
        Assert.NotNull(response.ProposedAction);
        Assert.Equal("save_memory_preview", response.ProposedAction!.ActionType);
        Assert.False(string.IsNullOrWhiteSpace(response.ProposedAction.ActionId));
        Assert.False(string.IsNullOrWhiteSpace(response.ProposedAction.Title));
        Assert.False(string.IsNullOrWhiteSpace(response.ProposedAction.Summary));
        Assert.Equal("medium", response.ProposedAction.RiskLevel);
        Assert.True(response.ProposedAction.RequiresConfirmation);
        Assert.Empty(response.ToolCalls);
        Assert.Equal(0, response.StepsUsed);
    }

    [Fact]
    public async Task AgentRunner_ReminderPreviewIntent_ReturnsReminderProposedAction()
    {
        var runner = CreateRunner(new FakeDocumentRepository());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "明天提醒我观察黑猫" },
            CancellationToken.None);

        Assert.True(response.RequiresConfirmation);
        Assert.NotNull(response.ProposedAction);
        Assert.Equal("create_reminder_preview", response.ProposedAction!.ActionType);
        Assert.Equal("明天观察黑猫状态", response.ProposedAction.Title);
    }

    [Fact]
    public async Task AgentRunner_RagIntent_CallsAnswerWithRagAndPreservesCitations()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 }
        };
        var ragChatService = new FakeRagChatService
        {
            ResponseToReturn = new RagChatResponse
            {
                Response = "根据文档，答案是 A [1]。",
                CitationIntegrity = "valid",
                Citations = new List<CitationNode>
                {
                    new CitationNode
                    {
                        Index = 1,
                        DocumentId = "doc_a",
                        DocumentName = "a.md",
                        ChunkIndex = 0,
                        PageNumber = 1,
                        SnippetPreview = "答案是 A"
                    }
                }
            }
        };
        var runner = CreateRunner(repository, ragChatService: ragChatService);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest
            {
                Message = "根据文档回答：答案是什么？",
                DocumentIds = new List<string> { "doc_a" },
                ClientTimeZone = "Asia/Shanghai"
            },
            CancellationToken.None);

        Assert.Equal("preview_readonly_rag", response.Mode);
        Assert.Equal(1, response.StepsUsed);
        Assert.Equal("answer_with_rag", response.ToolCalls[0].ToolName);
        Assert.Equal("根据文档，答案是 A [1]。", response.Answer);
        Assert.Equal("valid", response.CitationIntegrity);
        Assert.Single(response.Citations);
        Assert.Equal("doc_a", response.Citations[0].DocumentId);
        Assert.Equal("user_a", ragChatService.LastUserId);
        Assert.Equal(new List<string> { "doc_a" }, ragChatService.LastRequest!.DocumentIds);
    }

    [Fact]
    public async Task AnswerWithRagTool_RejectsDocumentIdsThatDoNotBelongToCurrentUser()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_b"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_b", UserId = "user_b", FileName = "b.md", Status = "success", ChunkCount = 1 }
        };
        var ragChatService = new FakeRagChatService();
        var tool = new AnswerWithRagTool(ragChatService, repository);

        var result = await tool.ExecuteAsync(
            new AgentContext { UserId = "user_a", ConversationId = "agent_test" },
            JsonSerializer.SerializeToElement(new
            {
                question = "根据文档回答",
                documentIds = new[] { "doc_b" }
            }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found or access denied", result.ErrorMessage);
        Assert.Null(ragChatService.LastRequest);
        Assert.Equal("user_a", repository.LastGetUserId);
    }

    [Fact]
    public async Task AgentRunner_UnknownIntent_DoesNotCallRagTools()
    {
        var repository = new FakeDocumentRepository();
        var ragChatService = new FakeRagChatService();
        var ragSearchService = new FakeRagSearchService();
        var runner = CreateRunner(repository, ragSearchService, ragChatService);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我随便聊聊" },
            CancellationToken.None);

        Assert.Equal(0, response.StepsUsed);
        Assert.Empty(response.ToolCalls);
        Assert.False(ragChatService.WasCalled);
        Assert.False(ragSearchService.WasCalled);
    }

    [Fact]
    public async Task SearchDocumentsTool_RejectsDocumentIdsThatDoNotBelongToCurrentUser()
    {
        var ragSearchService = new FakeRagSearchService
        {
            ErrorToThrow = new KeyNotFoundException("Document doc_b not found or access denied.")
        };
        var registry = new ToolRegistry(new IAgentTool[] { new SearchDocumentsTool(ragSearchService) });
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            new AgentContext { UserId = "user_a" },
            "search_documents",
            JsonSerializer.SerializeToElement(new
            {
                query = "查一下文档",
                documentIds = new[] { "doc_b" }
            }),
            1,
            CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("Agent tool execution failed", result.ErrorMessage);
    }

    [Fact]
    public async Task AgentRunner_RagToolFailure_ReturnsStructuredFailure()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 }
        };
        var ragChatService = new FakeRagChatService
        {
            ErrorToThrow = new InvalidOperationException("rag failed")
        };
        var runner = CreateRunner(repository, ragChatService: ragChatService);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest
            {
                Message = "根据文档回答：测试",
                DocumentIds = new List<string> { "doc_a" }
            },
            CancellationToken.None);

        Assert.Equal(1, response.StepsUsed);
        Assert.Equal("failed", response.ToolCalls[0].Status);
        Assert.Contains("Agent tool execution failed", response.ToolCalls[0].ErrorMessage);
        Assert.Contains("只读 Agent 工具调用失败", response.Answer);
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
        var runner = new AgentRunner(executor, Options.Create(new AgentOptions { MaxIterations = 3 }), new InMemoryPendingAgentActionStore());
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.RunAgentPreviewAsync(
            context,
            new AgentRunRequest { Message = "hello" },
            runner,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentRunApiResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal("preview_readonly_rag", ok.Value.Data.Mode);
        Assert.Contains("支持文档列表、文档状态查询和只读 RAG 问答", ok.Value.Data.Answer);
        Assert.Empty(ok.Value.Data.ToolCalls);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_UnknownActionId_FailsSafely()
    {
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";
        var store = new InMemoryPendingAgentActionStore();

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = "missing", Decision = "confirm" },
            store);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("not_found", ok.Value.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_IgnoresRequestUserIdAndPreventsCrossUserConfirm()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = store.Create(
            "user_a",
            "create_reminder_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_b";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest
            {
                ActionId = pending.ProposedAction.ActionId,
                Decision = "confirm",
                UserId = "user_a"
            },
            store);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("not_found", ok.Value.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CancelReturnsCancelledAndWritesNoData()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = store.Create(
            "user_a",
            "create_life_event_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "cancel" },
            store);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal("cancelled", ok.Value.Status);
        Assert.Contains("No data was written", ok.Value.Message);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_ConfirmReturnsPreviewSuccessAndWritesNoData()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = store.Create(
            "user_a",
            "create_reminder_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal("preview_success", ok.Value.Status);
        Assert.Contains("No data was written", ok.Value.Message);
        var json = JsonSerializer.Serialize(ok.Value.Result);
        Assert.Contains("false", json);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_ExpiredActionCannotBeConfirmed()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = store.Create(
            "user_a",
            "create_reminder_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromSeconds(-1));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("expired", ok.Value.Status);
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

    private static AgentRunner CreateRunner(
        IDocumentRepository repository,
        IRagSearchService? ragSearchService = null,
        IRagChatService? ragChatService = null,
        IPendingAgentActionStore? pendingActions = null,
        int maxIterations = 3)
    {
        var tools = new IAgentTool[]
        {
            new ListDocumentsTool(repository),
            new GetDocumentStatusTool(repository),
            new SearchDocumentsTool(ragSearchService ?? new FakeRagSearchService()),
            new AnswerWithRagTool(ragChatService ?? new FakeRagChatService(), repository)
        };
        var registry = new ToolRegistry(tools);
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);
        return new AgentRunner(
            executor,
            Options.Create(new AgentOptions { MaxIterations = maxIterations }),
            pendingActions ?? new InMemoryPendingAgentActionStore());
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

    private sealed class FakeRagChatService : IRagChatService
    {
        public bool WasCalled { get; private set; }
        public string? LastUserId { get; private set; }
        public RagChatRequest? LastRequest { get; private set; }
        public RagChatResponse ResponseToReturn { get; set; } = new()
        {
            Response = "fake answer",
            CitationIntegrity = "valid"
        };
        public Exception? ErrorToThrow { get; set; }

        public Task<RagChatResponse> ProcessChatAsync(string userId, RagChatRequest request)
        {
            WasCalled = true;
            LastUserId = userId;
            LastRequest = request;
            if (ErrorToThrow != null)
            {
                throw ErrorToThrow;
            }

            return Task.FromResult(ResponseToReturn);
        }
    }

    private sealed class FakeRagSearchService : IRagSearchService
    {
        public bool WasCalled { get; private set; }
        public Exception? ErrorToThrow { get; set; }
        public List<VectorSearchResult> ResultsToReturn { get; set; } = new();

        public Task<List<VectorSearchResult>> SearchAsync(
            string userId,
            string query,
            IReadOnlyList<string>? documentIds,
            int? topK,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            if (ErrorToThrow != null)
            {
                throw ErrorToThrow;
            }

            return Task.FromResult(ResultsToReturn);
        }
    }
}

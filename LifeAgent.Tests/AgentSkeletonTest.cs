using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Models;
using LifeAgent.Api.Models.Agent;
using LifeAgent.Api.Models.LifeEvents;
using LifeAgent.Api.Models.Memories;
using LifeAgent.Api.Services;
using LifeAgent.Api.Services.Agent;
using LifeAgent.Api.Services.Agent.Tools;
using LifeAgent.Api.Services.LifeEvents;
using LifeAgent.Api.Services.Memories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    public void ToolRegistry_ExposesPhase72ReadOnlyMetadata()
    {
        var registry = new ToolRegistry(new IAgentTool[]
        {
            new StubAgentTool("list_documents")
        });

        Assert.True(registry.TryGetEntry("LIST_DOCUMENTS", out var entry));
        Assert.NotNull(entry);
        Assert.Equal(ToolCategories.ReadOnlyRetrieval, entry!.Category);
        Assert.Equal("medium_sensitive_read", entry.RiskLevel);
        Assert.True(entry.AuthRequired);
        Assert.True(entry.UserScoped);
        Assert.True(entry.ReadsData);
        Assert.False(entry.WritesData);
        Assert.False(entry.ExternalSideEffect);
        Assert.True(entry.TraceRequired);
        Assert.True(entry.AuditRequired);
        Assert.True(entry.IsReadOnlyEligible);
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
        Assert.True(result.NoWrite);
        Assert.False(result.WritesData);
        Assert.False(result.PendingActionCreated);
        Assert.False(result.ExternalSideEffect);
    }

    [Fact]
    public async Task ToolExecutor_ReadOnlyTool_EmitsPhase72TraceAndNoWriteContract()
    {
        var registry = new ToolRegistry(new IAgentTool[]
        {
            new StubAgentTool("list_documents")
        });
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            new AgentContext { UserId = "user_a", RunId = "run_1" },
            "list_documents",
            JsonSerializer.SerializeToElement(new { userId = "user_b" }),
            2,
            CancellationToken.None);

        Assert.Equal("success", result.Status);
        Assert.Equal("1.0", result.ToolVersion);
        Assert.Equal(ToolCategories.ReadOnlyRetrieval, result.Category);
        Assert.Equal("retrieval", result.CapabilityType);
        Assert.Equal("medium_sensitive_read", result.RiskLevel);
        Assert.Equal("run_1:tool:2", result.TraceId);
        Assert.True(result.TraceRequired);
        Assert.True(result.AuditRequired);
        Assert.True(result.NoWrite);
        Assert.False(result.WritesData);
        Assert.False(result.ExternalSideEffect);
        Assert.False(result.PendingActionCreated);
        Assert.False(result.ConfirmationRequired);
    }

    [Fact]
    public async Task ToolExecutor_WriteTool_FailsClosedBeforeExecution()
    {
        var writeTool = new StubAgentTool("create_life_event", AgentToolRisk.Write, requiresConfirmation: true);
        var registry = new ToolRegistry(new IAgentTool[] { writeTool });
        var executor = new ToolExecutor(registry, NullLogger<ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync(
            new AgentContext { UserId = "user_a", RunId = "run_1" },
            "create_life_event",
            JsonSerializer.SerializeToElement(new { title = "test" }),
            1,
            CancellationToken.None);

        Assert.Equal("failed", result.Status);
        Assert.Contains("not eligible for Phase 7.2 read-only direct execution", result.ErrorMessage);
        Assert.False(writeTool.WasCalled);
        Assert.True(result.NoWrite);
        Assert.False(result.WritesData);
        Assert.False(result.ExternalSideEffect);
        Assert.False(result.PendingActionCreated);
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
            new AgentRunRequest { Message = "随便聊聊今天的天气" },
            CancellationToken.None);

        Assert.Equal(0, response.StepsUsed);
        Assert.Empty(response.ToolCalls);
        Assert.Contains("支持文档列表、文档状态查询和只读 RAG 问答", response.Answer);
        Assert.False(writeTool.WasCalled);
    }

    [Fact]
    public async Task AgentRunner_MemoryRetrievalDisabled_FallbackPayloadDoesNotContainMemoryContext()
    {
        var provider = new RecordingMemoryContextProvider(MemoryRuntimeContext.Disabled());
        var runner = CreateRunner(new FakeDocumentRepository(), memoryContextProvider: provider);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "随便聊聊今天的天气" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.False(response.WroteData);
        Assert.Null(response.ProposedAction);
        Assert.Equal(1, provider.CallCount);

        var json = JsonSerializer.Serialize(response.Payload);
        Assert.DoesNotContain("memoryContext", json);
    }

    [Fact]
    public async Task AgentRunner_MemoryRetrievalDisabled_ToolPayloadDoesNotContainMemoryContext()
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 }
        };
        var provider = new RecordingMemoryContextProvider(MemoryRuntimeContext.Disabled());
        var runner = CreateRunner(repository, memoryContextProvider: provider);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "列出我的文档" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.False(response.WroteData);
        Assert.Null(response.ProposedAction);
        Assert.Single(response.ToolCalls);
        Assert.Equal(1, provider.CallCount);

        var json = JsonSerializer.Serialize(response.Payload);
        Assert.DoesNotContain("memoryContext", json);
    }

    [Fact]
    public async Task AgentRunner_MemoryRetrievalEnabled_DoesNotCreatePendingActionOrWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var provider = new RecordingMemoryContextProvider(new MemoryRuntimeContext
        {
            Enabled = true,
            Status = "ready",
            ResultCount = 1,
            MaxResults = 1,
            Results =
            [
                new MemoryRuntimeContextItem
                {
                    MemoryId = "mem_1",
                    MemoryType = "preference",
                    Score = 1,
                    Reason = "test"
                }
            ],
            FormattedContext = "- [preference] 喜欢早上写代码"
        });
        var runner = CreateRunner(
            new FakeDocumentRepository(),
            pendingActions: store,
            memoryContextProvider: provider);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "随便聊聊今天的天气" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.False(response.RequiresConfirmation);
        Assert.Null(response.ProposedAction);
        Assert.False(response.WroteData);
        Assert.Null(response.CreatedResourceId);
        Assert.Equal(1, provider.CallCount);

        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"status\":\"ready\"", json);
        Assert.Contains("\"resultCount\":1", json);
        Assert.DoesNotContain("喜欢早上写代码", json);
    }

    [Fact]
    public async Task AgentRunner_MemoryRetrievalFailure_FallsBackWithoutFailingRun()
    {
        var runner = CreateRunner(
            new FakeDocumentRepository(),
            memoryContextProvider: new ThrowingMemoryContextProvider());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "随便聊聊今天的天气" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.False(response.WroteData);
        Assert.Null(response.ProposedAction);

        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"skippedReason\":\"retrieval_failed\"", json);
    }

    [Theory]
    [InlineData("请新增一条 life_event 生活事件记录：今天黑猫吐了一次", "create_life_event", true, "preview_confirmation")]
    [InlineData("帮我保存记忆：我喜欢早上写代码", "save_memory_preview", true, "preview_confirmation")]
    [InlineData("明天提醒我观察黑猫", "reminder_action", true, "preview_confirmation")]
    [InlineData("根据文档回答：项目目标是什么", "preview_readonly_rag", false, "preview_readonly_rag")]
    [InlineData("列出我的文档", "document_action", false, "preview_readonly_rag")]
    [InlineData("随便聊聊一个未知话题", "preview_readonly_rag", false, "preview_readonly_rag")]
    public async Task AgentRunner_IntentCoverageMatrix_ReturnsStableContract(
        string message,
        string expectedActionType,
        bool expectedRequiresConfirmation,
        string expectedMode)
    {
        var repository = new FakeDocumentRepository();
        repository.Documents["user_a"] = new List<KnowledgeDocument>
        {
            new KnowledgeDocument { Id = "doc_a", UserId = "user_a", FileName = "a.md", Status = "success", ChunkCount = 1 }
        };
        var runner = CreateRunner(repository);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = message, DocumentIds = new List<string> { "doc_a" } },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal(expectedMode, response.Mode);
        Assert.Equal(expectedActionType, response.ActionType);
        Assert.Equal(expectedRequiresConfirmation, response.RequiresConfirmation);
        Assert.True(response.PreviewOnly);
        Assert.False(response.WroteData);
        Assert.Null(response.CreatedResourceId);
    }

    [Fact]
    public async Task AgentRunner_LifeEventContract_IsPreviewOnlyAndDoesNotWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var runner = CreateRunner(new FakeDocumentRepository(), pendingActions: store);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "请新增一条 life_event 生活事件记录：今天黑猫吐了一次" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("create_life_event", response.ActionType);
        Assert.True(response.RequiresConfirmation);
        Assert.True(response.PreviewOnly);
        Assert.False(response.WroteData);
        Assert.Null(response.CreatedResourceId);
        Assert.NotNull(response.ProposedAction);
        Assert.Equal("create_life_event", response.ProposedAction!.ActionType);

        var stored = await store.GetAsync("user_a", response.ProposedAction.ActionId);
        Assert.NotNull(stored);
        Assert.False(stored!.WroteData);
        Assert.True(stored.PreviewOnly);
    }

    [Fact]
    public async Task AgentRunner_MemoryIntent_DoesNotTriggerLifeEvent()
    {
        var runner = CreateRunner(new FakeDocumentRepository());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：我喜欢早上写代码" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("save_memory_preview", response.ActionType);
        Assert.True(response.RequiresConfirmation);
        Assert.NotNull(response.ProposedAction);
        Assert.Equal("save_memory_preview", response.ProposedAction!.ActionType);
        Assert.NotEqual("create_life_event", response.ProposedAction.ActionType);
    }

    [Fact]
    public async Task AgentRunner_MemoryIntent_ReturnsPhase62PreviewPayloadSchema()
    {
        var store = new InMemoryPendingAgentActionStore();
        var runner = CreateRunner(new FakeDocumentRepository(), pendingActions: store);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：我喜欢早上写代码" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("save_memory_preview", response.ActionType);
        Assert.True(response.RequiresConfirmation);
        Assert.Null(response.CreatedResourceId);
        Assert.NotNull(response.ProposedAction);
        Assert.True(response.ProposedAction!.RequiresConfirmation);

        var payload = MemoryPreviewActionPayloadMapper.Map(response.ProposedAction.Payload);
        Assert.Equal("preference", payload.MemoryType);
        Assert.Equal("我喜欢早上写代码", payload.Content);
        Assert.Equal(0.8, payload.Confidence);
        Assert.Equal(3, payload.Importance);
        Assert.Equal("agent_preview", payload.Source);
        Assert.True(payload.PreviewOnly);
        Assert.Equal("帮我保存记忆：我喜欢早上写代码", payload.OriginalMessage);
        Assert.Equal("帮我保存记忆：我喜欢早上写代码", payload.SourceText);
        Assert.NotNull(payload.Metadata);
        Assert.Null(payload.ExpiresAt);
        Assert.Null(payload.GuardDecision);
        Assert.Null(payload.Blocked);
        Assert.Null(payload.ReviewRequired);
        MemoryPreviewActionPayloadMapper.ValidatePreviewPayload(response.ProposedAction.Payload);

        var payloadJson = JsonSerializer.Serialize(response.ProposedAction.Payload);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var payloadRoot = payloadDoc.RootElement;
        Assert.Equal("preference", payloadRoot.GetProperty("MemoryType").GetString());
        Assert.Equal("我喜欢早上写代码", payloadRoot.GetProperty("Content").GetString());
        Assert.Equal(0.8, payloadRoot.GetProperty("Confidence").GetDouble());
        Assert.Equal(3, payloadRoot.GetProperty("Importance").GetInt32());
        Assert.Equal("agent_preview", payloadRoot.GetProperty("Source").GetString());
        Assert.True(payloadRoot.GetProperty("PreviewOnly").GetBoolean());
        Assert.False(payloadRoot.TryGetProperty("GuardDecision", out _));
        Assert.False(payloadRoot.TryGetProperty("Blocked", out _));
        Assert.False(payloadRoot.TryGetProperty("ReviewRequired", out _));
        Assert.False(payloadRoot.TryGetProperty("GuardReason", out _));
        Assert.False(payloadRoot.TryGetProperty("ConflictResult", out _));
        Assert.False(payloadRoot.TryGetProperty("MergeCandidate", out _));

        var stored = await store.GetAsync("user_a", response.ProposedAction.ActionId);
        Assert.NotNull(stored);
        Assert.True(stored!.PreviewOnly);
        Assert.False(stored.WroteData);
        Assert.Null(stored.CreatedResourceId);
    }

    [Fact]
    public async Task AgentRunner_MemoryProposalGuardAllowed_CreatesPendingAction()
    {
        var store = new InMemoryPendingAgentActionStore();
        var guard = new FixedMemoryProposalGuard(new MemoryPollutionDecision
        {
            Action = "allow",
            Reason = "test_allow"
        });
        var runner = CreateRunner(
            new FakeDocumentRepository(),
            pendingActions: store,
            memoryProposalGuard: guard,
            memoryProposalOptions: EnabledMemoryProposalOptions());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：我喜欢早上写代码" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("save_memory_preview", response.ActionType);
        Assert.True(response.RequiresConfirmation);
        Assert.False(response.WroteData);
        Assert.NotNull(response.ProposedAction);
        Assert.Equal(1, guard.CallCount);

        var payload = MemoryPreviewActionPayloadMapper.Map(response.ProposedAction!.Payload);
        Assert.Equal("allow", payload.GuardDecision);
        Assert.Equal("test_allow", payload.GuardReason);
        Assert.Null(payload.Blocked);
        Assert.Null(payload.ReviewRequired);
        MemoryPreviewActionPayloadMapper.ValidatePreviewPayload(response.ProposedAction.Payload);

        var payloadJson = JsonSerializer.Serialize(response.ProposedAction.Payload);
        Assert.Contains("\"GuardDecision\":\"allow\"", payloadJson);
        Assert.Contains("\"GuardReason\":\"test_allow\"", payloadJson);
        Assert.Contains("\"PreviewOnly\":true", payloadJson);
        Assert.DoesNotContain("Blocked", payloadJson);
        Assert.DoesNotContain("ReviewRequired", payloadJson);

        var stored = await store.GetAsync("user_a", response.ProposedAction.ActionId);
        Assert.NotNull(stored);
        Assert.False(stored!.WroteData);
        Assert.True(stored.PreviewOnly);
    }

    [Fact]
    public async Task AgentRunner_MemoryProposalGuardBlocked_DoesNotCreatePendingAction()
    {
        var store = new InMemoryPendingAgentActionStore();
        var runner = CreateRunner(
            new FakeDocumentRepository(),
            pendingActions: store,
            memoryProposalGuard: new MemoryProposalGuard(),
            memoryProposalOptions: EnabledMemoryProposalOptions());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：Bearer abc.def.ghi" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("save_memory_preview", response.ActionType);
        Assert.Null(response.ProposedAction);
        Assert.False(response.WroteData);
        Assert.Null(response.CreatedResourceId);

        var json = JsonSerializer.Serialize(response.Payload);
        Assert.Contains("\"blocked\":true", json);
        Assert.Contains("\"guardDecision\":\"block\"", json);
        Assert.Contains("\"wroteData\":false", json);
        Assert.DoesNotContain("Bearer abc.def.ghi", json);
    }

    [Fact]
    public async Task AgentRunner_MemoryProposalGuardReviewRequired_RecordsConflictWithoutWriting()
    {
        var store = new InMemoryPendingAgentActionStore();
        var guard = new FixedMemoryProposalGuard(new MemoryPollutionDecision
        {
            Action = "review_required",
            ReviewRequired = true,
            Reason = "conflict_detected",
            ConflictResult = new MemoryConflictResult
            {
                HasConflict = true,
                ExistingMemoryId = "mem_existing",
                MemoryType = "preference",
                ConflictKind = "preference_polarity",
                Reason = "test_conflict"
            }
        });
        var runner = CreateRunner(
            new FakeDocumentRepository(),
            pendingActions: store,
            memoryProposalGuard: guard,
            memoryProposalOptions: EnabledMemoryProposalOptions());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：我不喜欢 coffee" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.NotNull(response.ProposedAction);
        Assert.False(response.WroteData);
        Assert.Null(response.CreatedResourceId);

        var payload = MemoryPreviewActionPayloadMapper.Map(response.ProposedAction!.Payload);
        Assert.Equal("review_required", payload.GuardDecision);
        Assert.True(payload.ReviewRequired);
        Assert.Null(payload.Blocked);
        Assert.NotNull(payload.ConflictResult);
        Assert.True(payload.ConflictResult!.HasConflict);
        Assert.Null(payload.MergeCandidate);
        MemoryPreviewActionPayloadMapper.ValidatePreviewPayload(response.ProposedAction.Payload);

        var payloadJson = JsonSerializer.Serialize(response.ProposedAction.Payload);
        Assert.Contains("\"GuardDecision\":\"review_required\"", payloadJson);
        Assert.Contains("\"ReviewRequired\":true", payloadJson);
        Assert.Contains("\"ConflictResult\"", payloadJson);
        Assert.Contains("\"ExistingMemoryId\":\"mem_existing\"", payloadJson);
        Assert.DoesNotContain("MergeCandidate", payloadJson);
    }

    [Fact]
    public async Task AgentRunner_MemoryProposalGuardMergeCandidate_RecordsCandidateWithoutAutoMerge()
    {
        var store = new InMemoryPendingAgentActionStore();
        var guard = new FixedMemoryProposalGuard(new MemoryPollutionDecision
        {
            Action = "merge_candidate",
            ReviewRequired = true,
            Reason = "duplicate_like_proposal",
            MergeCandidate = new MemoryMergeCandidate
            {
                HasCandidate = true,
                ExistingMemoryId = "mem_existing",
                MemoryType = "preference",
                SimilarityScore = 0.9,
                Reason = "test_duplicate"
            }
        });
        var runner = CreateRunner(
            new FakeDocumentRepository(),
            pendingActions: store,
            memoryProposalGuard: guard,
            memoryProposalOptions: EnabledMemoryProposalOptions());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：我喜欢 coffee" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.NotNull(response.ProposedAction);
        Assert.False(response.WroteData);
        Assert.Null(response.CreatedResourceId);

        var payload = MemoryPreviewActionPayloadMapper.Map(response.ProposedAction!.Payload);
        Assert.Equal("merge_candidate", payload.GuardDecision);
        Assert.True(payload.ReviewRequired);
        Assert.NotNull(payload.MergeCandidate);
        Assert.True(payload.MergeCandidate!.HasCandidate);
        Assert.Null(payload.ConflictResult);
        MemoryPreviewActionPayloadMapper.ValidatePreviewPayload(response.ProposedAction.Payload);

        var payloadJson = JsonSerializer.Serialize(response.ProposedAction.Payload);
        Assert.Contains("\"GuardDecision\":\"merge_candidate\"", payloadJson);
        Assert.Contains("\"ReviewRequired\":true", payloadJson);
        Assert.Contains("\"MergeCandidate\"", payloadJson);
        Assert.Contains("\"ExistingMemoryId\":\"mem_existing\"", payloadJson);
        Assert.DoesNotContain("ConflictResult", payloadJson);
    }

    [Fact]
    public async Task AgentRunner_MemoryConstraintPreview_ForcesImportanceFive()
    {
        var runner = CreateRunner(new FakeDocumentRepository());

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：我对花生过敏，禁止推荐花生食品" },
            CancellationToken.None);

        var payload = MemoryPreviewActionPayloadMapper.Map(response.ProposedAction!.Payload);
        Assert.Equal("constraint", payload.MemoryType);
        Assert.Equal(5, payload.Importance);
        MemoryPreviewActionPayloadMapper.ValidatePreviewPayload(response.ProposedAction.Payload);
    }

    [Fact]
    public void MemoryPreviewContractValidator_RejectsInvalidPayload()
    {
        var validator = new AgentContractValidator();
        var contract = new AgentExecutionContract(
            AgentIntentNames.Memory,
            1.0,
            AgentActionTypes.SaveMemoryPreview,
            RequiresConfirmation: true,
            IsFallback: false,
            FallbackReason: null);
        var proposedAction = new AgentProposedAction
        {
            ActionId = "agent_action_invalid",
            ActionType = AgentActionTypes.SaveMemoryPreview,
            Title = "保存一条记忆",
            Summary = "invalid",
            RequiresConfirmation = true,
            Payload = new MemoryPreviewActionPayload
            {
                MemoryType = "constraint",
                Content = "禁止推荐花生食品",
                Confidence = 0.9,
                Importance = 3,
                Source = "agent_preview",
                PreviewOnly = true,
                OriginalMessage = "禁止推荐花生食品"
            }
        };

        var result = validator.Validate(contract, AgentExecutionResult.Confirmation(proposedAction));

        Assert.False(result.Success);
        Assert.Contains("constraint", result.ErrorMessage);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_SaveMemoryPreviewConfirmsWithoutDurableMemoryWrite()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "save_memory_preview",
            "保存一条记忆",
            "preview only",
            new MemoryPreviewActionPayload
            {
                MemoryType = "preference",
                Content = "我喜欢早上写代码",
                Confidence = 0.8,
                Importance = 3,
                Source = "agent_preview",
                PreviewOnly = true,
                OriginalMessage = "帮我保存记忆：我喜欢早上写代码"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            WriteGate(new Dictionary<string, string?>
            {
                ["ENABLE_AGENT_WRITE_TOOLS"] = "true",
                ["ENABLE_CREATE_LIFE_EVENT_TOOL"] = "true"
            }),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        Assert.False(pending.WroteData);
        Assert.False(pending.WriteCompleted);
        Assert.Null(pending.CreatedResourceId);
        AssertPreviewOnly(ok.Value.Result);
        AssertCreatedResourceIdNull(ok.Value.Result);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_InvalidSaveMemoryPreviewPayloadFailsBeforeLifecycleTransition()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "save_memory_preview",
            "保存一条记忆",
            "invalid",
            new MemoryPreviewActionPayload
            {
                MemoryType = "preference",
                Content = "我喜欢早上写代码",
                Confidence = 0.8,
                Importance = 3,
                Source = "agent_preview",
                PreviewOnly = true,
                OriginalMessage = "帮我保存记忆：我喜欢早上写代码",
                Metadata = new Dictionary<string, object>
                {
                    ["api_key"] = "secret"
                }
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("invalid_payload", ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.Null(pending.ConfirmedAt);
        Assert.False(pending.WroteData);
        AssertPreviewOnly(ok.Value.Result);
    }

    [Fact]
    public async Task PendingActionStore_SaveMemoryPreviewRejectsWriteCompleted()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "save_memory_preview",
            "保存一条记忆",
            "preview only",
            new MemoryPreviewActionPayload
            {
                MemoryType = "preference",
                Content = "我喜欢早上写代码",
                Confidence = 0.8,
                Importance = 3,
                Source = "agent_preview",
                PreviewOnly = true,
                OriginalMessage = "帮我保存记忆：我喜欢早上写代码"
            },
            "medium",
            TimeSpan.FromMinutes(10));

        var response = await store.ConfirmWriteCompletedAsync(
            "user_a",
            pending.ProposedAction.ActionId,
            "memory",
            "mem_blocked");

        Assert.False(response.Success);
        Assert.Equal("preview_only", response.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.False(pending.WroteData);
        Assert.Null(pending.CreatedResourceId);
    }

    [Fact]
    public async Task AgentRunner_InvalidProposedActionType_ReturnsContractErrorWithoutFallback()
    {
        var runner = new AgentRunner(
            new ToolExecutor(new ToolRegistry(Array.Empty<IAgentTool>()), NullLogger<ToolExecutor>.Instance),
            Options.Create(new AgentOptions { MaxIterations = 3 }),
            new InvalidPendingActionStore("invalid_action_type", requiresConfirmation: true));

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：测试" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("preview_contract_error", response.Mode);
        Assert.Equal("invalid_action", response.ActionType);
        Assert.False(response.RequiresConfirmation);
        Assert.Null(response.ProposedAction);
        Assert.Empty(response.ToolCalls);
    }

    [Fact]
    public async Task AgentRunner_ProposedActionWithoutConfirmation_ReturnsContractError()
    {
        var runner = new AgentRunner(
            new ToolExecutor(new ToolRegistry(Array.Empty<IAgentTool>()), NullLogger<ToolExecutor>.Instance),
            Options.Create(new AgentOptions { MaxIterations = 3 }),
            new InvalidPendingActionStore("save_memory_preview", requiresConfirmation: false));

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest { Message = "帮我保存记忆：测试" },
            CancellationToken.None);

        AssertContractShape(response);
        Assert.Equal("preview_contract_error", response.Mode);
        Assert.Equal("invalid_action", response.ActionType);
        Assert.False(response.WroteData);
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
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, response.ProposedAction.LifecycleStatus);
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
    public async Task AgentRunner_LifeEventPreviewIntent_ReturnsValidCreateLifeEventProposedAction()
    {
        var store = new InMemoryPendingAgentActionStore();
        var runner = CreateRunner(new FakeDocumentRepository(), pendingActions: store);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest
            {
                Message = "[SMOKE TEST] 请新增一条 life_event 生活事件记录：今天黑猫吐了一次。type=pet_health，title=黑猫呕吐观察，content=今天黑猫吐了一次，暂时观察精神和食欲。"
            },
            CancellationToken.None);

        Assert.Equal("preview_confirmation", response.Mode);
        Assert.True(response.RequiresConfirmation);
        Assert.NotNull(response.ProposedAction);
        Assert.Equal("create_life_event", response.ProposedAction!.ActionType);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, response.ProposedAction.LifecycleStatus);
        Assert.True(response.ProposedAction.RequiresConfirmation);

        var request = LifeEventActionPayloadMapper.Map(response.ProposedAction.Payload);
        Assert.Equal("pet_health", request.Type);
        Assert.Equal("黑猫呕吐观察", request.Title);
        Assert.Contains("黑猫吐了一次", request.Content);
        Assert.Equal("黑猫", request.StructuredData["catName"]);
        Assert.False(JsonSerializer.Serialize(response.ProposedAction.Payload).Contains("userId", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AgentRunner_LifeEventPreviewIntent_FlagsOffConfirmsPreviewOnly()
    {
        var store = new InMemoryPendingAgentActionStore();
        var runner = CreateRunner(new FakeDocumentRepository(), pendingActions: store);

        var response = await runner.RunAsync(
            "user_a",
            new AgentRunRequest
            {
                Message = "[SMOKE TEST] 请新增一条 life_event 生活事件记录：今天黑猫吐了一次。type=pet_health，title=黑猫呕吐观察，content=今天黑猫吐了一次，暂时观察精神和食欲。"
            },
            CancellationToken.None);
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = response.ProposedAction!.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.LifecycleStatus);
        AssertPreviewOnly(ok.Value.Result);
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
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("not_found", ok.Value.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_IgnoresRequestUserIdAndPreventsCrossUserConfirm()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
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
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("not_found", ok.Value.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CancelReturnsCancelledAndWritesNoData()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
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
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, ok.Value.LifecycleStatus);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, pending.Status);
        Assert.Contains("No data was written", ok.Value.Message);

        var secondResult = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);
        var secondOk = Assert.IsType<Ok<AgentConfirmationResponse>>(secondResult);
        Assert.False(secondOk.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, secondOk.Value.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_ConfirmReturnsConfirmedAndWritesNoData()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
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
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.LifecycleStatus);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        Assert.Contains("No data was written", ok.Value.Message);
        var json = JsonSerializer.Serialize(ok.Value.Result);
        Assert.Contains("false", json);

        var secondResult = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);
        var secondOk = Assert.IsType<Ok<AgentConfirmationResponse>>(secondResult);
        Assert.True(secondOk.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, secondOk.Value.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_ExpiredActionCannotBeConfirmed()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
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
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Expired, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Expired, pending.Status);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CreateLifeEventFlagsOffConfirmsPreviewOnly()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。",
                structuredData = new
                {
                    tags = new[] { "猫", "健康" },
                    catName = "黑猫",
                    importance = 2
                }
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.LifecycleStatus);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        AssertPreviewOnly(ok.Value.Result);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CreateLifeEventForbiddenPayloadFailsBeforeLifecycleTransition()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。",
                userId = "payload_user"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("invalid_payload", ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, ok.Value.LifecycleStatus);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.Null(pending.ConfirmedAt);
        AssertPreviewOnly(ok.Value.Result);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CreateLifeEventFlagsTrueWritesThroughCoordinator()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";
        var lifeEvents = new RecordingAgentLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            WriteGate(new Dictionary<string, string?>
            {
                ["ENABLE_AGENT_WRITE_TOOLS"] = "true",
                ["ENABLE_CREATE_LIFE_EVENT_TOOL"] = "true"
            }),
            coordinator,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.True(ok.Value!.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, ok.Value.LifecycleStatus);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        Assert.Equal(1, lifeEvents.CallCount);
        AssertWriteResult(ok.Value.Result, pending.CreatedResourceId!, idempotent: false);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CreateLifeEventFlagsTrueDuplicateConfirmIsIdempotent()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";
        var lifeEvents = new RecordingAgentLifeEventService();
        var coordinator = CreateCoordinator(store, lifeEvents);
        var writeGate = WriteGate(new Dictionary<string, string?>
        {
            ["ENABLE_AGENT_WRITE_TOOLS"] = "true",
            ["ENABLE_CREATE_LIFE_EVENT_TOOL"] = "true"
        });

        var first = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            writeGate,
            coordinator,
            NullLoggerFactory.Instance,
            CancellationToken.None);
        var second = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            writeGate,
            coordinator,
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var firstOk = Assert.IsType<Ok<AgentConfirmationResponse>>(first);
        var secondOk = Assert.IsType<Ok<AgentConfirmationResponse>>(second);
        Assert.True(firstOk.Value!.Success);
        Assert.True(secondOk.Value!.Success);
        Assert.Equal(1, lifeEvents.CallCount);
        AssertWriteResult(secondOk.Value.Result, pending.CreatedResourceId!, idempotent: true);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_CreateLifeEventFlagsTrueWriteFailureDoesNotConfirm()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";
        var lifeEvents = new RecordingAgentLifeEventService
        {
            ErrorToThrow = new InvalidOperationException("firestore unavailable")
        };

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            WriteGate(new Dictionary<string, string?>
            {
                ["ENABLE_AGENT_WRITE_TOOLS"] = "true",
                ["ENABLE_CREATE_LIFE_EVENT_TOOL"] = "true"
            }),
            CreateCoordinator(store, lifeEvents),
            NullLoggerFactory.Instance,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("write_failed", ok.Value.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.Null(pending.ConfirmedAt);
        Assert.False(pending.WroteData);
        AssertPreviewOnly(ok.Value.Result);
    }

    [Fact]
    public async Task AgentConfirmEndpoint_LogsStructuredConfirmFieldsWithoutPayload()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event",
            "黑猫状态",
            "记录黑猫状态",
            new
            {
                type = "cat_health",
                title = "黑猫呕吐",
                content = "今天黑猫吐了一次，精神还可以。",
                secret = "do-not-log"
            },
            "medium",
            TimeSpan.FromMinutes(10));
        var context = new DefaultHttpContext();
        context.Items["userId"] = "user_a";
        var loggerFactory = new RecordingLoggerFactory();

        var result = await AgentEndpoints.ConfirmAgentActionAsync(
            context,
            new AgentConfirmationRequest { ActionId = pending.ProposedAction.ActionId, Decision = "confirm" },
            store,
            DefaultWriteGate(),
            CreateCoordinator(store),
            loggerFactory,
            CancellationToken.None);

        var ok = Assert.IsType<Ok<AgentConfirmationResponse>>(result);
        Assert.False(ok.Value!.Success);
        Assert.Equal("invalid_payload", ok.Value.Status);
        Assert.Contains(loggerFactory.Messages, item => item.Contains("Agent confirm request received", StringComparison.Ordinal));
        Assert.Contains(loggerFactory.Messages, item => item.Contains("FeatureGateCanCreateLifeEvent=False", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(loggerFactory.Messages, item => item.Contains("ErrorCode=invalid_payload", StringComparison.Ordinal));
        Assert.Contains(loggerFactory.Messages, item => item.Contains($"ActionId={pending.ProposedAction.ActionId}", StringComparison.Ordinal));
        Assert.DoesNotContain(loggerFactory.Messages, item => item.Contains("今天黑猫吐了一次", StringComparison.Ordinal));
        Assert.DoesNotContain(loggerFactory.Messages, item => item.Contains("do-not-log", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingActionStore_CreateAddsAuditFieldsAndPreviewOnly()
    {
        var store = new InMemoryPendingAgentActionStore();

        var pending = await store.CreateAsync(
            "user_a",
            "create_reminder_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));

        Assert.Equal("user_a", pending.UserId);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
        Assert.True(pending.PreviewOnly);
        Assert.True(pending.CreatedAt > DateTimeOffset.MinValue);
        Assert.True(pending.UpdatedAt >= pending.CreatedAt);
        Assert.Null(pending.ConfirmedAt);
        Assert.Null(pending.CancelledAt);
        Assert.Null(pending.ExpiredAt);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.ProposedAction.LifecycleStatus);
        Assert.True(pending.ProposedAction.ExpiresAt > pending.CreatedAt);
    }

    [Fact]
    public async Task PendingActionStore_ConfirmIsPreviewOnlyAndIdempotent()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_reminder_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));

        var first = await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "confirm");
        var second = await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "confirm");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, first.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, second.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Confirmed, pending.Status);
        Assert.NotNull(pending.ConfirmedAt);
        Assert.Null(pending.CancelledAt);
        AssertPreviewOnly(first.Result);
        AssertPreviewOnly(second.Result);
        Assert.Contains("true", JsonSerializer.Serialize(second.Result));
    }

    [Fact]
    public async Task PendingActionStore_CancelIsPreviewOnlyAndIdempotent()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "create_life_event_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));

        var first = await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "cancel");
        var second = await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "cancel");
        var conflictingConfirm = await store.ConfirmAsync("user_a", pending.ProposedAction.ActionId, "confirm");

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False(conflictingConfirm.Success);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, first.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, second.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Cancelled, pending.Status);
        Assert.NotNull(pending.CancelledAt);
        Assert.Null(pending.ConfirmedAt);
        AssertPreviewOnly(first.Result);
        AssertPreviewOnly(second.Result);
    }

    [Fact]
    public async Task PendingActionStore_PreventsCrossUserConfirmation()
    {
        var store = new InMemoryPendingAgentActionStore();
        var pending = await store.CreateAsync(
            "user_a",
            "save_memory_preview",
            "title",
            "summary",
            new { previewOnly = true },
            "medium",
            TimeSpan.FromMinutes(10));

        var response = await store.ConfirmAsync("user_b", pending.ProposedAction.ActionId, "confirm");

        Assert.False(response.Success);
        Assert.Equal("not_found", response.Status);
        Assert.Equal(InMemoryPendingAgentActionStore.Pending, pending.Status);
    }

    private static void AssertPreviewOnly(object? result)
    {
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"previewOnly\":true", json);
        Assert.Contains("\"wroteData\":false", json);
    }

    private static void AssertCreatedResourceIdNull(object? result)
    {
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("createdResourceId", out var createdResourceId));
        Assert.Equal(JsonValueKind.Null, createdResourceId.ValueKind);
    }

    private static void AssertContractShape(AgentRunResponse response)
    {
        Assert.False(string.IsNullOrWhiteSpace(response.RunId));
        Assert.False(string.IsNullOrWhiteSpace(response.Mode));
        Assert.NotNull(response.Answer);
        Assert.False(string.IsNullOrWhiteSpace(response.ActionType));
        Assert.NotNull(response.Payload);
        Assert.NotNull(response.ToolCalls);
        Assert.True(response.PreviewOnly);
        Assert.False(response.WroteData);
    }

    private static IAgentWriteFeatureGate DefaultWriteGate()
    {
        return WriteGate(new Dictionary<string, string?>());
    }

    private static IAgentWriteFeatureGate WriteGate(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new AgentWriteFeatureGate(configuration);
    }

    private static AgentLifeEventConfirmationWriteCoordinator CreateCoordinator(
        IPendingAgentActionStore pendingActions,
        IAgentLifeEventService? lifeEventService = null)
    {
        return new AgentLifeEventConfirmationWriteCoordinator(
            pendingActions,
            lifeEventService ?? new RecordingAgentLifeEventService(),
            NullLogger<AgentLifeEventConfirmationWriteCoordinator>.Instance);
    }

    private static void AssertWriteResult(object? result, string createdResourceId, bool idempotent)
    {
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"previewOnly\":false", json);
        Assert.Contains("\"wroteData\":true", json);
        Assert.Contains("\"createdResourceType\":\"life_event\"", json);
        Assert.Contains($"\"createdResourceId\":\"{createdResourceId}\"", json);
        Assert.Contains($"\"idempotent\":{idempotent.ToString().ToLowerInvariant()}", json);
    }

    private sealed class RecordingAgentLifeEventService : IAgentLifeEventService
    {
        public int CallCount { get; private set; }
        public Exception? ErrorToThrow { get; set; }

        public Task<LifeEvent> CreateFromAgentConfirmationAsync(
            string authenticatedUserId,
            string agentActionId,
            CreateLifeEventRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (ErrorToThrow is not null)
            {
                throw ErrorToThrow;
            }

            return Task.FromResult(new LifeEvent
            {
                Id = $"evt_{agentActionId}",
                UserId = authenticatedUserId,
                Type = request.Type,
                Title = request.Title,
                Content = request.Content,
                Source = "agent_confirmed",
                CreatedBy = "agent",
                AgentActionId = agentActionId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<string> Messages { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(Messages);
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _messages;

        public RecordingLogger(List<string> messages)
        {
            _messages = messages;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
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

    private sealed class InvalidPendingActionStore : IPendingAgentActionStore
    {
        private readonly string _actionType;
        private readonly bool _requiresConfirmation;

        public InvalidPendingActionStore(string actionType, bool requiresConfirmation)
        {
            _actionType = actionType;
            _requiresConfirmation = requiresConfirmation;
        }

        public Task<PendingAgentAction> CreateAsync(
            string userId,
            string actionType,
            string title,
            string summary,
            object payload,
            string riskLevel,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PendingAgentAction
            {
                UserId = userId,
                Status = InMemoryPendingAgentActionStore.Pending,
                PreviewOnly = true,
                WroteData = false,
                ProposedAction = new AgentProposedAction
                {
                    ActionId = "act_invalid",
                    ActionType = _actionType,
                    Title = title,
                    Summary = summary,
                    Payload = payload,
                    RiskLevel = riskLevel,
                    RequiresConfirmation = _requiresConfirmation,
                    LifecycleStatus = InMemoryPendingAgentActionStore.Pending,
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = DateTimeOffset.UtcNow.Add(ttl)
                }
            });
        }

        public Task<PendingAgentAction?> GetAsync(string userId, string actionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<PendingAgentAction?>(null);
        }

        public Task<AgentConfirmationResponse> ConfirmAsync(string userId, string actionId, string decision, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AgentConfirmationResponse> ConfirmWriteCompletedAsync(
            string userId,
            string actionId,
            string createdResourceType,
            string createdResourceId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private static AgentRunner CreateRunner(
        IDocumentRepository repository,
        IRagSearchService? ragSearchService = null,
        IRagChatService? ragChatService = null,
        IPendingAgentActionStore? pendingActions = null,
        int maxIterations = 3,
        IMemoryContextProvider? memoryContextProvider = null,
        IMemoryProposalGuard? memoryProposalGuard = null,
        MemoryProposalRuntimeOptions? memoryProposalOptions = null)
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
            pendingActions ?? new InMemoryPendingAgentActionStore(),
            memoryContextProvider,
            memoryProposalGuard,
            memoryProposalOptions is null ? null : Options.Create(memoryProposalOptions));
    }

    private static MemoryProposalRuntimeOptions EnabledMemoryProposalOptions()
    {
        return new MemoryProposalRuntimeOptions
        {
            EnableMemoryProposalRuntime = true,
            EnableMemoryProposalGuard = true
        };
    }

    private sealed class RecordingMemoryContextProvider : IMemoryContextProvider
    {
        private readonly MemoryRuntimeContext _context;
        public int CallCount { get; private set; }

        public RecordingMemoryContextProvider(MemoryRuntimeContext context)
        {
            _context = context;
        }

        public Task<MemoryRuntimeContext> GetContextAsync(
            MemoryContextRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_context);
        }
    }

    private sealed class ThrowingMemoryContextProvider : IMemoryContextProvider
    {
        public Task<MemoryRuntimeContext> GetContextAsync(
            MemoryContextRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("retrieval failed");
        }
    }

    private sealed class FixedMemoryProposalGuard : IMemoryProposalGuard
    {
        private readonly MemoryPollutionDecision _decision;
        public int CallCount { get; private set; }

        public FixedMemoryProposalGuard(MemoryPollutionDecision decision)
        {
            _decision = decision;
        }

        public MemoryPollutionDecision Evaluate(
            MemoryPreviewActionPayload proposal,
            IReadOnlyList<Memory> existingMemories)
        {
            CallCount++;
            return _decision;
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

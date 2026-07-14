using System.Text.Json;
using LifeAgent.Api.Endpoints;
using LifeAgent.Api.Services.Agent.PendingActions;
using LifeAgent.Api.Services.Agent.Phase8;
using LifeAgent.Api.Services.Agent.UnifiedInbox;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace LifeAgent.Tests;

public class Phase9PendingActionPersistenceTest
{
    [Fact]
    public async Task StoreCreateCanBeReadAfterRuntimeRefresh()
    {
        var store = new InMemoryPendingActionStore();
        var runtimeA = new Phase80PendingActionRuntime(store: store);
        var created = await runtimeA.CreateAsync("user_a", new Phase80CreatePendingActionRequest("持久动作", "刷新后仍可读取"));

        var runtimeB = new Phase80PendingActionRuntime(store: store);
        var restored = await runtimeB.ListAsync("user_a");

        Assert.True(created.Success);
        Assert.Single(restored);
        Assert.Equal(created.Data!.ActionId, restored[0].ActionId);
        Assert.Equal("pending", restored[0].Status);
        Assert.Equal("持久动作", restored[0].Title);
        Assert.False(restored[0].Executed);
        Assert.False(restored[0].WroteData);
    }

    [Fact]
    public async Task ConfirmStatusPersistsAndDoesNotExecute()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var confirmed = await runtime.ConfirmAsync("user_a", created.Data!.ActionId);
        var restored = await new Phase80PendingActionRuntime(store: store).ListAsync("user_a");

        Assert.True(confirmed.Success);
        Assert.Equal("confirmed", confirmed.Data!.Status);
        Assert.Single(restored);
        Assert.Equal("confirmed", restored[0].Status);
        Assert.False(restored[0].Executed);
        Assert.False(restored[0].WroteData);
        Assert.False(restored[0].ExecutionReady);
        Assert.False(restored[0].LegacyConfirmEndpointUsed);
        Assert.False(restored[0].RealWritePath);
    }

    [Fact]
    public async Task CancelStatusPersistsAndCannotConfirm()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var cancelled = await runtime.CancelAsync("user_a", created.Data!.ActionId);
        var confirmAfterCancel = await runtime.ConfirmAsync("user_a", created.Data.ActionId);
        var restored = await new Phase80PendingActionRuntime(store: store).ListAsync("user_a");

        Assert.True(cancelled.Success);
        Assert.Equal("cancelled", cancelled.Data!.Status);
        Assert.False(confirmAfterCancel.Success);
        Assert.Equal("cancelled", confirmAfterCancel.Status);
        Assert.Single(restored);
        Assert.Equal("cancelled", restored[0].Status);
        Assert.False(restored[0].Executed);
    }

    [Fact]
    public async Task CrossUserAccessIsBlocked()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var listForOtherUser = await runtime.ListAsync("user_b");
        var confirmForOtherUser = await runtime.ConfirmAsync("user_b", created.Data!.ActionId);

        Assert.Empty(listForOtherUser);
        Assert.False(confirmForOtherUser.Success);
        Assert.Equal("not_found", confirmForOtherUser.Status);
    }

    [Fact]
    public async Task ArchiveHidesOwnedHistoryWithoutDeletingOrExecuting()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest("旧测试记录", "隐藏而不是硬删除"));
        var confirmed = await runtime.ConfirmAsync("user_a", created.Data!.ActionId);

        var archived = await runtime.ArchiveAsync("user_a", confirmed.Data!.ActionId);
        var hiddenList = await runtime.ListAsync("user_a");
        var stored = await store.GetByIdAsync("user_a", confirmed.Data.ActionId);

        Assert.True(archived.Success);
        Assert.True(archived.Data!.IsArchived);
        Assert.Empty(hiddenList);
        Assert.NotNull(stored);
        Assert.True(stored!.IsArchived);
        Assert.Equal("user_a", stored.ArchivedByUserId);
        Assert.False(stored.Executed);
        Assert.False(stored.WroteData);
    }

    [Fact]
    public async Task ArchiveIsOwnerScoped()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest(null, null));

        var otherUserArchive = await runtime.ArchiveAsync("user_b", created.Data!.ActionId);
        var stillVisible = await runtime.ListAsync("user_a");

        Assert.False(otherUserArchive.Success);
        Assert.Equal("not_found", otherUserArchive.Status);
        Assert.Single(stillVisible);
        Assert.False(stillVisible[0].IsArchived);
    }

    [Fact]
    public async Task EndpointIgnoresBodyUserIdAndUsesAuthContext()
    {
        var services = BuildServices();
        var request = new Phase80CreatePendingActionRequest(
            "body cannot set owner",
            "malicious userId=user_b should not be trusted");
        var context = AuthenticatedContext("user_a", services);

        var create = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(context, request));
        var actionId = ReadString(create.Body, "data", "actionId");
        var userAList = await ExecuteResultAsync(AgentEndpoints.ListPhase80PendingActionsAsync(AuthenticatedContext("user_a", services)));
        var userBConfirm = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(AuthenticatedContext("user_b", services), actionId));

        Assert.Equal(StatusCodes.Status200OK, create.StatusCode);
        Assert.Contains(actionId, userAList.Body);
        Assert.Equal(StatusCodes.Status404NotFound, userBConfirm.StatusCode);
        Assert.Equal("not_found", ReadString(userBConfirm.Body, "status"));
    }

    [Fact]
    public void PersistenceOptionsDefaultToInMemoryAndRequireExplicitFirestoreApproval()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRuntime:PendingActionStore:Mode"] = "firestore"
            })
            .Build();

        var options = PendingActionPersistenceOptions.FromConfiguration(configuration);

        Assert.Equal(PendingActionPersistenceOptions.ModeFirestore, options.Mode);
        Assert.False(options.AllowFirestore);
        Assert.False(options.UseFirestore);
        Assert.Equal("personal_agent_v2_in_memory_preview_only", options.SafetyMode);
    }

    [Fact]
    public void PersistenceOptionsUseFirestoreOnlyWhenModeAndApprovalAreExplicit()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRuntime:PendingActionStore:Mode"] = "firestore",
                ["AgentRuntime:PendingActionStore:AllowFirestore"] = "true",
                ["AgentRuntime:PendingActionStore:PreviewOnly"] = "true"
            })
            .Build();

        var options = PendingActionPersistenceOptions.FromConfiguration(configuration);

        Assert.True(options.AllowFirestore);
        Assert.True(options.UseFirestore);
        Assert.True(options.PreviewOnly);
        Assert.Equal("personal_agent_v2_firestore_persistence_preview_only", options.SafetyMode);
    }

    [Fact]
    public void PersistenceOptionsReadCloudRunEnvStyleKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AGENT_PENDING_ACTION_STORE_MODE"] = "firestore",
                ["AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE"] = "true",
                ["AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY"] = "true"
            })
            .Build();

        var options = PendingActionPersistenceOptions.FromConfiguration(configuration);

        Assert.Equal(PendingActionPersistenceOptions.ModeFirestore, options.Mode);
        Assert.True(options.AllowFirestore);
        Assert.True(options.PreviewOnly);
        Assert.True(options.UseFirestore);
        Assert.Equal("personal_agent_v2_firestore_persistence_preview_only", options.SafetyMode);
    }

    [Fact]
    public void PersistenceOptionsInvalidEnvStyleKeysFailSafe()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AGENT_PENDING_ACTION_STORE_MODE"] = "unknown",
                ["AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE"] = "not-a-bool",
                ["AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY"] = "not-a-bool"
            })
            .Build();

        var options = PendingActionPersistenceOptions.FromConfiguration(configuration);

        Assert.Equal(PendingActionPersistenceOptions.ModeInMemory, options.Mode);
        Assert.False(options.AllowFirestore);
        Assert.True(options.PreviewOnly);
        Assert.False(options.UseFirestore);
        Assert.Equal("personal_agent_v2_in_memory_preview_only", options.SafetyMode);
    }

    [Fact]
    public void PersistenceOptionsRequirePreviewOnlyForFirestorePersistence()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AgentRuntime:PendingActionStore:Mode"] = "firestore",
                ["AgentRuntime:PendingActionStore:AllowFirestore"] = "true",
                ["AgentRuntime:PendingActionStore:PreviewOnly"] = "false"
            })
            .Build();

        var options = PendingActionPersistenceOptions.FromConfiguration(configuration);

        Assert.Equal(PendingActionPersistenceOptions.ModeFirestore, options.Mode);
        Assert.True(options.AllowFirestore);
        Assert.False(options.PreviewOnly);
        Assert.False(options.UseFirestore);
        Assert.Equal("personal_agent_v2_in_memory_preview_only", options.SafetyMode);
    }

    [Fact]
    public void StoreFactoryDefaultsToInMemoryForSafeAndRollbackModes()
    {
        var timeProvider = TimeProvider.System;
        static Google.Cloud.Firestore.FirestoreDb UnexpectedFirestore()
        {
            throw new InvalidOperationException("Firestore should not be requested for safe in-memory modes.");
        }

        var defaultStore = PendingActionStoreFactory.Create(new PendingActionPersistenceOptions(), UnexpectedFirestore, timeProvider);
        var disabledStore = PendingActionStoreFactory.Create(
            new PendingActionPersistenceOptions
            {
                Mode = PendingActionPersistenceOptions.ModeDisabled,
                AllowFirestore = true
            },
            UnexpectedFirestore,
            timeProvider);
        var unapprovedFirestore = PendingActionStoreFactory.Create(
            new PendingActionPersistenceOptions
            {
                Mode = PendingActionPersistenceOptions.ModeFirestore,
                AllowFirestore = false
            },
            UnexpectedFirestore,
            timeProvider);
        var notPreviewOnly = PendingActionStoreFactory.Create(
            new PendingActionPersistenceOptions
            {
                Mode = PendingActionPersistenceOptions.ModeFirestore,
                AllowFirestore = true,
                PreviewOnly = false
            },
            UnexpectedFirestore,
            timeProvider);

        Assert.IsType<InMemoryPendingActionStore>(defaultStore);
        Assert.IsType<InMemoryPendingActionStore>(disabledStore);
        Assert.IsType<InMemoryPendingActionStore>(unapprovedFirestore);
        Assert.IsType<InMemoryPendingActionStore>(notPreviewOnly);
    }

    [Fact]
    public void StoreFactorySelectsFirestoreOnlyForExplicitApprovedMode()
    {
        var firestore = Google.Cloud.Firestore.FirestoreDb.Create("test-project");
        var store = PendingActionStoreFactory.Create(
            new PendingActionPersistenceOptions
            {
                Mode = PendingActionPersistenceOptions.ModeFirestore,
                AllowFirestore = true,
                PreviewOnly = true
            },
            () => firestore,
            TimeProvider.System);

        Assert.IsType<FirestorePendingActionStore>(store);
    }

    [Fact]
    public void StoreFactoryResolvesFirestoreOnlyAfterPersistenceGate()
    {
        var firestoreRequested = false;
        Google.Cloud.Firestore.FirestoreDb FirestoreFactory()
        {
            firestoreRequested = true;
            return Google.Cloud.Firestore.FirestoreDb.Create("test-project");
        }

        var defaultStore = PendingActionStoreFactory.Create(
            Options.Create(new PendingActionPersistenceOptions()),
            FirestoreFactory,
            TimeProvider.System);

        Assert.IsType<InMemoryPendingActionStore>(defaultStore);
        Assert.False(firestoreRequested);

        var firestoreStore = PendingActionStoreFactory.Create(
            Options.Create(new PendingActionPersistenceOptions
            {
                Mode = PendingActionPersistenceOptions.ModeFirestore,
                AllowFirestore = true,
                PreviewOnly = true
            }),
            FirestoreFactory,
            TimeProvider.System);

        Assert.IsType<FirestorePendingActionStore>(firestoreStore);
        Assert.True(firestoreRequested);
    }

    [Fact]
    public async Task EndpointUsesInjectedPendingActionStoreForPersonalAgentV2()
    {
        var store = new InMemoryPendingActionStore();
        var services = BuildServices(store);
        var create = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            AuthenticatedContext("user_a", services),
            new Phase80CreatePendingActionRequest("DI backed", "same store across endpoint calls")));
        var actionId = ReadString(create.Body, "data", "actionId");

        var fromStore = await store.GetByIdAsync("user_a", actionId);
        var listed = await ExecuteResultAsync(AgentEndpoints.ListPhase80PendingActionsAsync(AuthenticatedContext("user_a", services)));

        Assert.NotNull(fromStore);
        Assert.Contains("DI backed", listed.Body);
        Assert.Equal("personal_agent_v2_in_memory_preview_only", ReadString(create.Body, "data", "safetyMode"));
    }

    [Fact]
    public async Task ListEndpointReturnsPersistenceMetadataWithoutActions()
    {
        var services = BuildServices();

        var listed = await ExecuteResultAsync(AgentEndpoints.ListPhase80PendingActionsAsync(
            AuthenticatedContext("user_a", services)));

        Assert.Equal(StatusCodes.Status200OK, listed.StatusCode);
        Assert.Equal(PendingActionPersistenceOptions.ModeInMemory, ReadString(listed.Body, "persistence", "storeMode"));
        Assert.False(ReadBool(listed.Body, "persistence", "firestorePersistenceEnabled"));
        Assert.True(ReadBool(listed.Body, "persistence", "previewOnly"));
        Assert.Equal("personal_agent_v2_in_memory_preview_only", ReadString(listed.Body, "persistence", "safetyMode"));
    }

    [Fact]
    public async Task ConfirmDoesNotModifyPayloadSnapshot()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest("original", "snapshot"));

        var before = await store.GetByIdAsync("user_a", created.Data!.ActionId);
        var confirmed = await runtime.ConfirmAsync("user_a", created.Data.ActionId);
        var after = await store.GetByIdAsync("user_a", created.Data.ActionId);

        Assert.True(confirmed.Success);
        Assert.Equal("confirmed", confirmed.Data!.Status);
        Assert.Equal(before!.Payload["title"], after!.Payload["title"]);
        Assert.Equal(before.Payload["summary"], after.Payload["summary"]);
        Assert.False(after.Executed);
        Assert.False(after.WroteData);
    }

    [Fact]
    public async Task StoreCreateRejectsDuplicateActionIdWithoutPayloadOverwrite()
    {
        var store = new InMemoryPendingActionStore();
        var first = await store.CreateAsync(CreateStoreRequest(
            "pa_duplicate",
            "user_a",
            title: "original title",
            summary: "original summary",
            idempotencyKeyHash: "idem_original"));
        var duplicate = await store.CreateAsync(CreateStoreRequest(
            "pa_duplicate",
            "user_a",
            title: "overwritten title",
            summary: "overwritten summary",
            idempotencyKeyHash: "idem_different"));
        var stored = await store.GetByIdAsync("user_a", "pa_duplicate");

        Assert.True(first.Success);
        Assert.False(duplicate.Success);
        Assert.Equal("duplicate_pending_action", duplicate.ErrorCode);
        Assert.Equal("original title", stored!.Payload["title"]);
        Assert.Equal("original summary", stored.Payload["summary"]);
        Assert.False(stored.Executed);
        Assert.False(stored.WroteData);
    }

    [Fact]
    public async Task StoreCreateRejectsMissingAuditMetadata()
    {
        var store = new InMemoryPendingActionStore();
        var missingIdempotency = await store.CreateAsync(CreateStoreRequest(
            "pa_missing_idempotency",
            "user_a",
            title: "missing idempotency",
            summary: "must not be stored",
            idempotencyKeyHash: string.Empty));
        var missingAuditRef = await store.CreateAsync(CreateStoreRequest(
            "pa_missing_audit",
            "user_a",
            title: "missing audit",
            summary: "must not be stored",
            idempotencyKeyHash: "idem_missing_audit",
            auditEventRefs: Array.Empty<string>()));
        var storedIdempotency = await store.GetByIdAsync("user_a", "pa_missing_idempotency");
        var storedAudit = await store.GetByIdAsync("user_a", "pa_missing_audit");

        Assert.False(missingIdempotency.Success);
        Assert.Equal("invalid_audit_metadata", missingIdempotency.ErrorCode);
        Assert.False(missingAuditRef.Success);
        Assert.Equal("invalid_audit_metadata", missingAuditRef.ErrorCode);
        Assert.Null(storedIdempotency);
        Assert.Null(storedAudit);
    }

    [Fact]
    public async Task StoreKeysPendingActionIdsByOwner()
    {
        var store = new InMemoryPendingActionStore();
        var userA = await store.CreateAsync(CreateStoreRequest(
            "pa_shared_id",
            "user_a",
            title: "user a title",
            summary: "user a summary",
            idempotencyKeyHash: "idem_a"));
        var userB = await store.CreateAsync(CreateStoreRequest(
            "pa_shared_id",
            "user_b",
            title: "user b title",
            summary: "user b summary",
            idempotencyKeyHash: "idem_b"));

        var userARecord = await store.GetByIdAsync("user_a", "pa_shared_id");
        var userBRecord = await store.GetByIdAsync("user_b", "pa_shared_id");
        var userAList = await store.QueryAsync(new PendingActionQuery("user_a"));
        var userBList = await store.QueryAsync(new PendingActionQuery("user_b"));

        Assert.True(userA.Success);
        Assert.True(userB.Success);
        Assert.Equal("user a title", userARecord!.Payload["title"]);
        Assert.Equal("user b title", userBRecord!.Payload["title"]);
        Assert.Single(userAList);
        Assert.Single(userBList);
        Assert.Equal("user_a", userAList[0].UserSubjectRef);
        Assert.Equal("user_b", userBList[0].UserSubjectRef);
    }

    [Fact]
    public async Task EndpointStatusMutationIsScopedByOwnerWhenActionIdsMatch()
    {
        var store = new InMemoryPendingActionStore();
        await store.CreateAsync(CreateStoreRequest(
            "pa_shared_endpoint_id",
            "user_a",
            title: "user a title",
            summary: "user a summary",
            idempotencyKeyHash: "idem_endpoint_a"));
        await store.CreateAsync(CreateStoreRequest(
            "pa_shared_endpoint_id",
            "user_b",
            title: "user b title",
            summary: "user b summary",
            idempotencyKeyHash: "idem_endpoint_b"));
        var services = BuildServices(store);

        var userBConfirm = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
            AuthenticatedContext("user_b", services),
            "pa_shared_endpoint_id"));
        var userARecord = await store.GetByIdAsync("user_a", "pa_shared_endpoint_id");
        var userBRecord = await store.GetByIdAsync("user_b", "pa_shared_endpoint_id");

        Assert.Equal(StatusCodes.Status200OK, userBConfirm.StatusCode);
        Assert.Equal("confirmed", ReadString(userBConfirm.Body, "data", "status"));
        Assert.Equal(PendingActionStatus.ConfirmationRequired, userARecord!.Status);
        Assert.Equal(PendingActionStatus.Confirmed, userBRecord!.Status);
        Assert.Equal("user a title", userARecord.Payload["title"]);
        Assert.Equal("user b title", userBRecord.Payload["title"]);
        Assert.False(userARecord.Executed);
        Assert.False(userBRecord.Executed);
        Assert.False(userARecord.WroteData);
        Assert.False(userBRecord.WroteData);
    }

    [Fact]
    public async Task EndpointReturnsConflictWithActionViewForFinalizedState()
    {
        var services = BuildServices();
        var context = AuthenticatedContext("user_a", services);
        var create = await ExecuteResultAsync(AgentEndpoints.CreatePhase80PendingActionAsync(
            context,
            new Phase80CreatePendingActionRequest("http conflict", "finalized state")));
        var actionId = ReadString(create.Body, "data", "actionId");

        var confirm = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
            AuthenticatedContext("user_a", services),
            actionId));
        var cancelAfterConfirm = await ExecuteResultAsync(AgentEndpoints.CancelPhase80PendingActionAsync(
            AuthenticatedContext("user_a", services),
            actionId));

        Assert.Equal(StatusCodes.Status200OK, create.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, confirm.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict, cancelAfterConfirm.StatusCode);
        Assert.False(ReadBool(cancelAfterConfirm.Body, "success"));
        Assert.Equal("confirmed", ReadString(cancelAfterConfirm.Body, "status"));
        Assert.Equal("confirmed", ReadString(cancelAfterConfirm.Body, "data", "status"));
        Assert.False(ReadBool(cancelAfterConfirm.Body, "data", "executed"));
        Assert.False(ReadBool(cancelAfterConfirm.Body, "data", "wroteData"));
    }

    [Fact]
    public async Task EndpointReturnsNotFoundForMissingPendingAction()
    {
        var missing = await ExecuteResultAsync(AgentEndpoints.ConfirmPhase80PendingActionAsync(
            AuthenticatedContext("user_a"),
            "missing_action"));

        Assert.Equal(StatusCodes.Status404NotFound, missing.StatusCode);
        Assert.False(ReadBool(missing.Body, "success"));
        Assert.Equal("not_found", ReadString(missing.Body, "status"));
    }

    [Fact]
    public async Task StoreRejectsDirectExecutedTransition()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest("no execute", "store guard"));

        var executeAttempt = await store.UpdateStatusAsync(new PendingActionStatusUpdate(
            PendingActionId: created.Data!.ActionId,
            UserSubjectRef: "user_a",
            ExpectedStatus: PendingActionStatus.ConfirmationRequired,
            NewStatus: PendingActionStatus.Executed));
        var after = await store.GetByIdAsync("user_a", created.Data.ActionId);

        Assert.False(executeAttempt.Success);
        Assert.Equal("execution_not_enabled", executeAttempt.ErrorCode);
        Assert.Equal(PendingActionStatus.ConfirmationRequired, after!.Status);
        Assert.False(after.Executed);
        Assert.False(after.WroteData);
    }

    [Fact]
    public async Task StoreRejectsTerminalMetadataAndGuardMutation()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var created = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest("terminal", "immutable"));

        await runtime.ConfirmAsync("user_a", created.Data!.ActionId);
        var metadataUpdate = await store.RecordConfirmationReferenceAsync(
            "user_a",
            created.Data.ActionId,
            confirmationId: "late_confirmation",
            confirmationRequestHash: "late_hash");
        var statusUpdate = await store.UpdateStatusAsync(new PendingActionStatusUpdate(
            PendingActionId: created.Data.ActionId,
            UserSubjectRef: "user_a",
            ExpectedStatus: PendingActionStatus.Confirmed,
            NewStatus: PendingActionStatus.Cancelled));
        var guardUpdate = await store.RecordGuardDecisionReferenceAsync(
            "user_a",
            created.Data.ActionId,
            guardDecisionRef: "late_guard",
            status: PendingActionStatus.ExecutionBlocked,
            blockedReason: "late_block");
        var after = await store.GetByIdAsync("user_a", created.Data.ActionId);

        Assert.False(metadataUpdate.Success);
        Assert.Equal("terminal_status", metadataUpdate.ErrorCode);
        Assert.False(statusUpdate.Success);
        Assert.Equal("terminal_status", statusUpdate.ErrorCode);
        Assert.False(guardUpdate.Success);
        Assert.Equal("terminal_status", guardUpdate.ErrorCode);
        Assert.Equal(PendingActionStatus.Confirmed, after!.Status);
        Assert.False(after.Executed);
        Assert.False(after.WroteData);
    }

    [Fact]
    public void TransitionPolicyRejectsUnsafeStatusChanges()
    {
        var pending = PendingActionRecordFixture("user_a", "policy_pending") with
        {
            Status = PendingActionStatus.ConfirmationRequired
        };
        var cancelled = pending with
        {
            Status = PendingActionStatus.Cancelled
        };

        var mismatch = PendingActionTransitionPolicy.ValidateStatusUpdate(
            pending,
            new PendingActionStatusUpdate(
                PendingActionId: pending.PendingActionId,
                UserSubjectRef: pending.UserSubjectRef,
                ExpectedStatus: PendingActionStatus.Confirmed,
                NewStatus: PendingActionStatus.Cancelled));
        var execute = PendingActionTransitionPolicy.ValidateStatusUpdate(
            pending,
            new PendingActionStatusUpdate(
                PendingActionId: pending.PendingActionId,
                UserSubjectRef: pending.UserSubjectRef,
                ExpectedStatus: PendingActionStatus.ConfirmationRequired,
                NewStatus: PendingActionStatus.Executed));
        var cancelledConfirm = PendingActionTransitionPolicy.ValidateTargetStatus(
            cancelled,
            PendingActionStatus.Confirmed);

        Assert.Equal("status_mismatch", mismatch!.ErrorCode);
        Assert.Equal("execution_not_enabled", execute!.ErrorCode);
        Assert.Equal("cancelled_cannot_confirm", cancelledConfirm!.ErrorCode);
    }

    [Fact]
    public async Task ListReturnsHistoricalConfirmedAndCancelledActions()
    {
        var store = new InMemoryPendingActionStore();
        var runtime = new Phase80PendingActionRuntime(store: store);
        var confirmed = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest("confirmed history", null));
        var cancelled = await runtime.CreateAsync("user_a", new Phase80CreatePendingActionRequest("cancelled history", null));
        var otherUser = await runtime.CreateAsync("user_b", new Phase80CreatePendingActionRequest("hidden", null));

        await runtime.ConfirmAsync("user_a", confirmed.Data!.ActionId);
        await runtime.CancelAsync("user_a", cancelled.Data!.ActionId);

        var history = await new Phase80PendingActionRuntime(store: store).ListAsync("user_a");

        Assert.Equal(2, history.Count);
        Assert.Contains(history, action => action.ActionId == confirmed.Data.ActionId && action.Status == "confirmed");
        Assert.Contains(history, action => action.ActionId == cancelled.Data.ActionId && action.Status == "cancelled");
        Assert.DoesNotContain(history, action => action.ActionId == otherUser.Data!.ActionId);
    }

    [Fact]
    public void FirestoreCandidateSerializesApprovedSchemaWithoutExecutionFlags()
    {
        var record = new PendingActionRecord
        {
            PendingActionId = "pa_schema",
            PreviewId = "preview_pa_schema",
            ToolId = "phase8_preview_tool",
            ToolVersion = "1.0",
            AdapterId = "phase8_preview_adapter",
            ActionType = "phase8_fake_pending_action",
            UserSubjectRef = "user_a",
            SessionSubjectRef = "agent_preview_default_session",
            RiskLevel = "low_preview_only",
            Status = PendingActionStatus.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash = "idem_pa_schema",
            InputHash = "input_pa_schema",
            PreviewHash = "preview_pa_schema",
            PolicySnapshotRef = "policy_pa_schema",
            TraceId = "trace_pa_schema",
            AuditEventRefs = new[] { "audit_created", "audit_confirmed" },
            SanitizedPreviewRef = "preview_ref_pa_schema",
            ServerOnlyPayloadRef = "payload_ref_pa_schema",
            Payload = new Dictionary<string, string>
            {
                ["title"] = "schema title",
                ["summary"] = "schema summary"
            },
            WroteData = true,
            Executed = true
        };

        var document = FirestorePendingActionStore.ToDocument(record);

        Assert.Equal("pa_schema", document["pendingActionId"]);
        Assert.Equal("user_a", document["userId"]);
        Assert.Equal("phase8_fake_pending_action", document["actionType"]);
        Assert.Equal(PendingActionStatus.Confirmed, document["status"]);
        Assert.True(document.ContainsKey("payload"));
        Assert.True(document.ContainsKey("createdAt"));
        Assert.True(document.ContainsKey("updatedAt"));
        Assert.True(document.ContainsKey("confirmedAt"));
        Assert.True(document.ContainsKey("cancelledAt"));
        Assert.True(document.ContainsKey("audit"));
        var audit = Assert.IsType<Dictionary<string, object?>>(document["audit"]);
        Assert.Equal("user_a", audit["createdByUserId"]);
        Assert.IsType<Google.Cloud.Firestore.Timestamp>(audit["updatedAt"]);
        Assert.Equal(new[] { "audit_created", "audit_confirmed" }, audit["refs"]);
        Assert.True((bool)document["executed"]!);
        Assert.True((bool)document["wroteData"]!);
    }

    [Fact]
    public void FirestoreCandidateRoundTripsPayloadAuditAndSafetyFields()
    {
        var record = new PendingActionRecord
        {
            PendingActionId = "pa_roundtrip",
            PreviewId = "preview_pa_roundtrip",
            ToolId = "phase8_preview_tool",
            ToolVersion = "1.0",
            AdapterId = "phase8_preview_adapter",
            ActionType = "phase8_fake_pending_action",
            UserSubjectRef = "user_a",
            SessionSubjectRef = "agent_preview_default_session",
            RiskLevel = "low_preview_only",
            Status = PendingActionStatus.Cancelled,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash = "idem_roundtrip",
            InputHash = "input_roundtrip",
            PreviewHash = "preview_roundtrip",
            PolicySnapshotRef = "policy_roundtrip",
            TraceId = "trace_roundtrip",
            AuditEventRefs = new[] { "audit_created", "audit_cancelled" },
            SanitizedPreviewRef = "preview_ref_roundtrip",
            ServerOnlyPayloadRef = "payload_ref_roundtrip",
            Payload = new Dictionary<string, string>
            {
                ["title"] = "roundtrip title",
                ["summary"] = "roundtrip summary"
            },
            RedactionMetadata = new Dictionary<string, string>
            {
                ["mode"] = "preview_only"
            },
            ValidationSnapshot = new Dictionary<string, string>
            {
                ["guardDecision"] = Phase80PendingActionRuntime.GuardDecision
            },
            CancellationReason = "user_cancelled",
            WroteData = true,
            Executed = true
        };

        var document = FirestorePendingActionStore.ToDocument(record);
        var restored = FirestorePendingActionStore.FromDictionary(document!);

        Assert.Equal("pa_roundtrip", restored.PendingActionId);
        Assert.Equal("user_a", restored.UserSubjectRef);
        Assert.Equal(PendingActionStatus.Cancelled, restored.Status);
        Assert.Equal("roundtrip title", restored.Payload["title"]);
        Assert.Equal("roundtrip summary", restored.Payload["summary"]);
        Assert.Equal("preview_only", restored.RedactionMetadata["mode"]);
        Assert.Equal(Phase80PendingActionRuntime.GuardDecision, restored.ValidationSnapshot["guardDecision"]);
        Assert.Equal(new[] { "audit_created", "audit_cancelled" }, restored.AuditEventRefs);
        Assert.Equal("user_cancelled", restored.CancellationReason);
        Assert.True(restored.Executed);
        Assert.True(restored.WroteData);
    }

    private static DefaultHttpContext AuthenticatedContext(string userId, IServiceProvider? services = null)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = services ?? BuildServices();
        context.Items["userId"] = userId;
        return context;
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = BuildServices();
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        body.Position = 0;
        using var reader = new StreamReader(body);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static async Task<(int StatusCode, string Body)> ExecuteResultAsync(Task<IResult> result)
    {
        return await ExecuteResultAsync(await result);
    }

    private static string ReadString(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement;
        foreach (var segment in path)
        {
            Assert.True(current.TryGetProperty(segment, out var next), $"Missing JSON property '{segment}'.");
            current = next;
        }

        return current.GetString() ?? string.Empty;
    }

    private static bool ReadBool(string json, params string[] path)
    {
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement;
        foreach (var segment in path)
        {
            Assert.True(current.TryGetProperty(segment, out var next), $"Missing JSON property '{segment}'.");
            current = next;
        }

        return current.GetBoolean();
    }

    private static IServiceProvider BuildServices(IPendingActionStore? store = null)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(_ => { });
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new PendingActionPersistenceOptions()));
        services.AddSingleton<IPendingActionStore>(store ?? new InMemoryPendingActionStore());
        services.AddSingleton<Phase80PendingActionRuntime>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<PendingActionPersistenceOptions>>().Value;
            return new Phase80PendingActionRuntime(
                timeProvider: sp.GetRequiredService<TimeProvider>(),
                store: sp.GetRequiredService<IPendingActionStore>(),
                safetyMode: options.SafetyMode);
        });
        services.AddSingleton<IUnifiedInboxRuntime, UnifiedInboxRuntime>();
        return services.BuildServiceProvider();
    }

    private static PendingActionRecord PendingActionRecordFixture(string userId, string id)
    {
        return new PendingActionRecord
        {
            PendingActionId = id,
            PreviewId = $"preview_{id}",
            ToolId = "phase8_preview_tool",
            ToolVersion = "1.0",
            AdapterId = "phase8_preview_adapter",
            ActionType = "phase8_fake_pending_action",
            UserSubjectRef = userId,
            SessionSubjectRef = "agent_preview_default_session",
            RiskLevel = "low_preview_only",
            Status = PendingActionStatus.ConfirmationRequired,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash = $"idem_{id}",
            InputHash = $"input_{id}",
            PreviewHash = $"preview_hash_{id}",
            PolicySnapshotRef = $"policy_{id}",
            TraceId = $"trace_{id}",
            AuditEventRefs = new[] { $"audit_{id}" },
            SanitizedPreviewRef = $"preview_ref_{id}",
            ServerOnlyPayloadRef = $"payload_ref_{id}",
            Payload = new Dictionary<string, string>
            {
                ["title"] = "fixture title",
                ["summary"] = "fixture summary"
            },
            WroteData = false,
            Executed = false
        };
    }

    private static PendingActionCreateRequest CreateStoreRequest(
        string id,
        string userId,
        string title,
        string summary,
        string idempotencyKeyHash,
        IReadOnlyList<string>? auditEventRefs = null)
    {
        return new PendingActionCreateRequest(
            PendingActionId: id,
            PreviewId: $"preview_{id}",
            ToolId: "phase8_preview_tool",
            ToolVersion: "1.0",
            AdapterId: "phase8_preview_adapter",
            ActionType: "phase8_fake_pending_action",
            UserSubjectRef: userId,
            SessionSubjectRef: "agent_preview_default_session",
            RiskLevel: "low_preview_only",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash: idempotencyKeyHash,
            InputHash: $"input_{id}_{idempotencyKeyHash}",
            PreviewHash: $"preview_hash_{id}_{idempotencyKeyHash}",
            PolicySnapshotRef: $"policy_{id}",
            TraceId: $"trace_{id}_{idempotencyKeyHash}",
            AuditEventRefs: auditEventRefs ?? new[] { $"audit_{id}_{idempotencyKeyHash}" },
            SanitizedPreviewRef: $"preview_ref_{id}",
            ServerOnlyPayloadRef: $"payload_ref_{id}",
            Payload: new Dictionary<string, string>
            {
                ["title"] = title,
                ["summary"] = summary
            },
            RedactionMetadata: new Dictionary<string, string>
            {
                ["mode"] = "preview_only"
            },
            ValidationSnapshot: new Dictionary<string, string>
            {
                ["guardDecision"] = Phase80PendingActionRuntime.GuardDecision
            });
    }
}

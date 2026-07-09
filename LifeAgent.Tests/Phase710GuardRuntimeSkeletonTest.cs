using System.Collections.Concurrent;
using LifeAgent.Api.Services.Agent.GuardedExecution;
using LifeAgent.Api.Services.Agent.PendingActions;

namespace LifeAgent.Tests;

public class Phase710GuardRuntimeSkeletonTest
{
    [Fact]
    public async Task Phase710_MissingPendingActionReturnsRejectedNotFound()
    {
        var runtime = new GuardedExecutionRuntime(new Phase710InMemoryPendingActionStore());

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("missing"));

        Assert.False(response.Success);
        Assert.Equal("not_found", response.Status);
        Assert.Equal(GuardDecisionType.RejectCrossUser, response.Decision);
        Assert.False(response.Executed);
        Assert.False(response.WroteData);
    }

    [Fact]
    public async Task Phase710_PendingActionOwnedByAnotherUserIsBlocked()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_cross", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_cross", user: "user_b"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectCrossUser, response.Decision);
        Assert.False(response.Executed);
    }

    [Fact]
    public async Task Phase710_ExpiredPendingActionIsRejectedExpired()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_expired", "user_a") with
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_expired"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectExpired, response.Decision);
        Assert.Equal(PendingActionStatus.Expired, response.Status);
        Assert.False(response.Executed);
    }

    [Fact]
    public async Task Phase710_CancelledPendingActionIsRejectedCancelled()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_cancelled", "user_a") with
        {
            Status = PendingActionStatus.Cancelled
        });
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_cancelled"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectCancelled, response.Decision);
        Assert.Equal("cancelled", response.BlockedReason);
    }

    [Fact]
    public async Task Phase710_ConfirmedWriteIntentIsBlockedWithoutReleaseGate()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_write", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_write", writeIntent: true));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectWriteIntent, response.Decision);
        Assert.False(response.ExecutionReady);
        Assert.False(response.Executed);
        Assert.False(response.WroteData);
    }

    [Fact]
    public async Task Phase710_ConfirmedExternalCallIsBlockedWithoutReleaseGate()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_external", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_external", externalCall: true));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectExternalCall, response.Decision);
        Assert.False(response.ExternalCallMade);
    }

    [Fact]
    public async Task Phase710_HighRiskActionRequiresReleaseGate()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_high", "user_a", riskLevel: "critical_release_gated"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_high", riskLevel: "critical_release_gated"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RequireReleaseGate, response.Decision);
        Assert.False(response.Executed);
    }

    [Fact]
    public async Task Phase710_StaleToolVersionIsBlocked()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_version", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_version", toolVersion: "2.0"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectToolVersionMismatch, response.Decision);
    }

    [Fact]
    public async Task Phase710_InputHashMismatchIsBlocked()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_input", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_input", inputHash: "bad_input_hash"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectHashMismatch, response.Decision);
    }

    [Fact]
    public async Task Phase710_PreviewHashMismatchIsBlocked()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_preview", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_preview", previewHash: "bad_preview_hash"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectHashMismatch, response.Decision);
    }

    [Fact]
    public async Task Phase710_MissingConfirmationIsBlocked()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_missing_confirmation", "user_a") with
        {
            ConfirmationId = null
        });
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_missing_confirmation"));

        Assert.False(response.Success);
        Assert.Equal(GuardDecisionType.RejectMissingConfirmation, response.Decision);
    }

    [Fact]
    public async Task Phase710_LowRiskNoWriteNoExternalCanReachExecutionReadyButNotExecuted()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_ready", "user_a", riskLevel: "medium_preview_only"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_ready", riskLevel: "medium_preview_only"));

        Assert.True(response.Success);
        Assert.True(response.ExecutionReady);
        Assert.Equal(GuardDecisionType.AllowExecutionReady, response.Decision);
        Assert.False(response.Executed);
        Assert.False(response.WroteData);
        Assert.False(response.ExternalCallMade);
    }

    [Fact]
    public async Task Phase710_ResponseDoesNotExposeServerOnlyPayload()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_projection", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_projection"));
        var responseText = System.Text.Json.JsonSerializer.Serialize(response);

        Assert.DoesNotContain("payload_ref", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullContext", responseText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phase710_BlockedReasonIsSanitized()
    {
        var store = new Phase710InMemoryPendingActionStore();
        await store.SeedAsync(Record("pa_reason", "user_a"));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(Request("pa_reason", writeIntent: true));

        Assert.Equal("write_intent_blocked", response.BlockedReason);
        Assert.DoesNotContain("secret", response.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", response.BlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw", response.BlockedReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phase710_ReleaseGateEvaluatorDefaultDeniesRealExecution()
    {
        var evaluator = new DenyAllReleaseGateEvaluator();

        var writeDecision = await evaluator.EvaluateAsync(new ReleaseGateEvaluationRequest(
            "mock_write",
            "1.0",
            "mock_write_action",
            "high_internal_write",
            WriteIntent: true,
            ExternalCall: false,
            "user_a"));
        var externalDecision = await evaluator.EvaluateAsync(new ReleaseGateEvaluationRequest(
            "mock_external",
            "1.0",
            "mock_external_action",
            "high_external_side_effect",
            WriteIntent: false,
            ExternalCall: true,
            "user_a"));

        Assert.False(writeDecision.AllowsWriteIntent);
        Assert.False(writeDecision.AllowsExecution);
        Assert.False(externalDecision.AllowsExternalCall);
        Assert.False(externalDecision.AllowsExecution);
    }

    private static GuardedExecutionRequest Request(
        string pendingActionId,
        string user = "user_a",
        string toolVersion = "1.0",
        string inputHash = "",
        string previewHash = "",
        string riskLevel = "medium_preview_only",
        bool writeIntent = false,
        bool externalCall = false)
    {
        return new GuardedExecutionRequest(
            UserSubjectRef: user,
            PendingActionId: pendingActionId,
            ConfirmationId: $"confirm_{pendingActionId}",
            ToolId: "mock_phase710_tool",
            ToolVersion: toolVersion,
            InputHash: string.IsNullOrWhiteSpace(inputHash) ? $"input_{pendingActionId}" : inputHash,
            PreviewHash: string.IsNullOrWhiteSpace(previewHash) ? $"preview_{pendingActionId}" : previewHash,
            ConfirmationHash: $"confirmation_{pendingActionId}",
            IdempotencyKeyHash: $"idem_{pendingActionId}",
            WriteIntent: writeIntent,
            ExternalCall: externalCall,
            RiskLevel: riskLevel,
            TraceId: $"trace_{pendingActionId}");
    }

    private static PendingActionRecord Record(
        string id,
        string user,
        string riskLevel = "medium_preview_only")
    {
        return new PendingActionRecord
        {
            PendingActionId = id,
            PreviewId = $"preview_id_{id}",
            ConfirmationId = $"confirm_{id}",
            ToolId = "mock_phase710_tool",
            ToolVersion = "1.0",
            AdapterId = "mock_phase710_adapter",
            ActionType = "mock_guarded_action",
            UserSubjectRef = user,
            SessionSubjectRef = "session_a",
            RiskLevel = riskLevel,
            Status = PendingActionStatus.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-30),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash = $"idem_{id}",
            InputHash = $"input_{id}",
            PreviewHash = $"preview_{id}",
            PolicySnapshotRef = $"policy_{id}",
            TraceId = $"trace_{id}",
            AuditEventRefs = new[] { $"audit_{id}_confirmed" },
            SanitizedPreviewRef = $"safe_preview_{id}",
            ServerOnlyPayloadRef = $"payload_ref_{id}",
            ValidationSnapshot = new Dictionary<string, string>
            {
                ["confirmationHash"] = $"confirmation_{id}"
            },
            RedactionMetadata = new Dictionary<string, string>
            {
                ["status"] = "sanitized"
            }
        };
    }

    private sealed class Phase710InMemoryPendingActionStore : IPendingActionStore
    {
        private readonly ConcurrentDictionary<string, PendingActionRecord> _records = new();

        public Task SeedAsync(PendingActionRecord record)
        {
            _records[record.PendingActionId] = record;
            return Task.CompletedTask;
        }

        public Task<PendingActionStoreResult> CreateAsync(PendingActionCreateRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Phase 7.10 guard tests seed records directly.");
        }

        public Task<PendingActionRecord?> GetByIdAsync(string userSubjectRef, string pendingActionId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _records.TryGetValue(pendingActionId, out var record) &&
                record.UserSubjectRef == userSubjectRef
                    ? record
                    : null);
        }

        public Task<IReadOnlyList<PendingActionClientView>> GetActiveByUserAsync(string userSubjectRef, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PendingActionClientView>>(
                _records.Values
                    .Where(record => record.UserSubjectRef == userSubjectRef && PendingActionStatus.IsActive(record.Status))
                    .Select(record => record.ToClientView())
                    .ToList());
        }

        public Task<PendingActionStoreResult> UpdateStatusAsync(PendingActionStatusUpdate update, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Phase 7.10 guard tests use guard decision updates only.");
        }

        public Task<PendingActionStoreResult> MarkExpiredAsync(string userSubjectRef, string pendingActionId, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            return UpdateGuardStatus(userSubjectRef, pendingActionId, PendingActionStatus.Expired, "expired", auditEventRef);
        }

        public Task<PendingActionStoreResult> CancelAsync(string userSubjectRef, string pendingActionId, string? cancellationReason = null, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            return UpdateGuardStatus(userSubjectRef, pendingActionId, PendingActionStatus.Cancelled, cancellationReason, auditEventRef);
        }

        public Task<PendingActionStoreResult> RecordConfirmationReferenceAsync(string userSubjectRef, string pendingActionId, string confirmationId, string confirmationRequestHash, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Phase 7.10 guard tests seed confirmation refs directly.");
        }

        public Task<PendingActionStoreResult> RecordGuardDecisionReferenceAsync(string userSubjectRef, string pendingActionId, string guardDecisionRef, string status, string? blockedReason = null, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            return UpdateGuardStatus(userSubjectRef, pendingActionId, status, blockedReason, auditEventRef, guardDecisionRef);
        }

        public Task<PendingActionStoreResult> CheckIdempotencyKeyHashAsync(string userSubjectRef, string idempotencyKeyHash, CancellationToken cancellationToken = default)
        {
            var record = _records.Values.FirstOrDefault(item => item.UserSubjectRef == userSubjectRef && item.IdempotencyKeyHash == idempotencyKeyHash);
            return Task.FromResult(record is null
                ? PendingActionStoreResult.Failed("not_found", "not_found", "Not found.")
                : PendingActionStoreResult.Succeeded(record, idempotent: true));
        }

        public Task<PendingActionRecord?> GetByPreviewIdAsync(string userSubjectRef, string previewId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.Values.FirstOrDefault(record => record.UserSubjectRef == userSubjectRef && record.PreviewId == previewId));
        }

        public Task<PendingActionRecord?> GetByConfirmationIdAsync(string userSubjectRef, string confirmationId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.Values.FirstOrDefault(record => record.UserSubjectRef == userSubjectRef && record.ConfirmationId == confirmationId));
        }

        public Task<IReadOnlyList<PendingActionRecord>> QueryAsync(PendingActionQuery query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PendingActionRecord>>(
                _records.Values.Where(record => record.UserSubjectRef == query.UserSubjectRef).ToList());
        }

        private Task<PendingActionStoreResult> UpdateGuardStatus(
            string userSubjectRef,
            string pendingActionId,
            string status,
            string? blockedReason,
            string? auditEventRef,
            string? guardDecisionRef = null)
        {
            if (!_records.TryGetValue(pendingActionId, out var record) || record.UserSubjectRef != userSubjectRef)
            {
                return Task.FromResult(PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found."));
            }

            var validation = new Dictionary<string, string>(record.ValidationSnapshot);
            if (!string.IsNullOrWhiteSpace(guardDecisionRef))
            {
                validation["guardDecisionRef"] = guardDecisionRef;
            }

            var updated = record with
            {
                Status = status,
                UpdatedAt = DateTimeOffset.UtcNow,
                BlockedReason = blockedReason,
                ValidationSnapshot = validation,
                AuditEventRefs = string.IsNullOrWhiteSpace(auditEventRef)
                    ? record.AuditEventRefs
                    : record.AuditEventRefs.Concat(new[] { auditEventRef }).ToList(),
                WroteData = false,
                Executed = false
            };
            _records[pendingActionId] = updated;
            return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
        }
    }
}

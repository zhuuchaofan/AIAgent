using System.Collections.Concurrent;
using LifeAgent.Api.Services.Agent.PendingActions;

namespace LifeAgent.Tests;

public class Phase79PendingActionStoreSkeletonTest
{
    [Fact]
    public async Task Phase79_CreatePendingActionAndReadById()
    {
        var store = new Phase79InMemoryPendingActionStore();
        var created = await store.CreateAsync(CreateRequest("pa_1", "user_a"));

        var found = await store.GetByIdAsync("user_a", "pa_1");

        Assert.True(created.Success);
        Assert.NotNull(found);
        Assert.Equal("pa_1", found.PendingActionId);
        Assert.Equal(PendingActionStatus.ConfirmationRequired, found.Status);
        Assert.False(found.WroteData);
        Assert.False(found.Executed);
    }

    [Fact]
    public async Task Phase79_ActiveQueryOnlyReturnsSameUserActiveActions()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_active", "user_a"));
        await store.CreateAsync(CreateRequest("pa_other", "user_b"));
        await store.CreateAsync(CreateRequest("pa_cancelled", "user_a"));
        await store.CancelAsync("user_a", "pa_cancelled", "user_cancelled");

        var active = await store.GetActiveByUserAsync("user_a");

        Assert.Single(active);
        Assert.Equal("pa_active", active[0].PendingActionId);
    }

    [Fact]
    public async Task Phase79_ExpiredActionIsNotActive()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_expired", "user_a", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)));

        var active = await store.GetActiveByUserAsync("user_a");
        var expired = await store.GetByIdAsync("user_a", "pa_expired");

        Assert.Empty(active);
        Assert.NotNull(expired);
        Assert.Equal(PendingActionStatus.Expired, expired.Status);
    }

    [Fact]
    public async Task Phase79_CancelledActionIsNotConfirmableOrActive()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_cancel", "user_a"));

        var cancelled = await store.CancelAsync("user_a", "pa_cancel", "user_cancelled");
        var confirm = await store.UpdateStatusAsync(new PendingActionStatusUpdate(
            "pa_cancel",
            "user_a",
            PendingActionStatus.ConfirmationRequired,
            PendingActionStatus.Confirmed));

        Assert.True(cancelled.Success);
        Assert.False(confirm.Success);
        Assert.Equal("status_mismatch", confirm.ErrorCode);
        Assert.Empty(await store.GetActiveByUserAsync("user_a"));
    }

    [Fact]
    public async Task Phase79_StatusUpdateRequiresExpectedStatus()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_status", "user_a"));

        var result = await store.UpdateStatusAsync(new PendingActionStatusUpdate(
            "pa_status",
            "user_a",
            PendingActionStatus.Confirmed,
            PendingActionStatus.ExecutionReady));

        Assert.False(result.Success);
        Assert.Equal("status_mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Phase79_DuplicateIdempotencyKeyReturnsExistingAction()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_first", "user_a", idempotencyKeyHash: "idem_same"));
        var duplicate = await store.CreateAsync(CreateRequest("pa_second", "user_a", idempotencyKeyHash: "idem_same"));

        Assert.True(duplicate.Success);
        Assert.True(duplicate.Idempotent);
        Assert.NotNull(duplicate.Record);
        Assert.Equal("pa_first", duplicate.Record.PendingActionId);
    }

    [Fact]
    public async Task Phase79_CrossUserReadReturnsNotFound()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_private", "user_a"));

        var found = await store.GetByIdAsync("user_b", "pa_private");

        Assert.Null(found);
    }

    [Fact]
    public async Task Phase79_ClientSafeProjectionDoesNotExposeServerOnlyPayload()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_projection", "user_a"));

        var active = await store.GetActiveByUserAsync("user_a");
        var projectedText = System.Text.Json.JsonSerializer.Serialize(active[0]);

        Assert.DoesNotContain("payload_ref", projectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("input_hash", projectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preview_hash", projectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", projectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", projectedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullContext", projectedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Phase79_AuditAndTraceRefsAreRetainedAsReferencesOnly()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_refs", "user_a"));

        var record = await store.GetByIdAsync("user_a", "pa_refs");

        Assert.NotNull(record);
        Assert.Equal("trace_pa_refs", record.TraceId);
        Assert.Contains("audit_pa_refs_created", record.AuditEventRefs);
        Assert.All(record.AuditEventRefs, item =>
        {
            Assert.DoesNotContain("secret", item, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw", item, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Phase79_ConfirmedIsNotExecuted()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_confirmed", "user_a"));

        var result = await store.UpdateStatusAsync(new PendingActionStatusUpdate(
            "pa_confirmed",
            "user_a",
            PendingActionStatus.ConfirmationRequired,
            PendingActionStatus.Confirmed,
            ConfirmationId: "confirm_pa_confirmed",
            AuditEventRef: "audit_confirmed"));

        Assert.True(result.Success);
        Assert.NotNull(result.Record);
        Assert.Equal(PendingActionStatus.Confirmed, result.Record.Status);
        Assert.False(result.Record.WroteData);
        Assert.False(result.Record.Executed);
    }

    [Fact]
    public async Task Phase79_ExecutionReadyIsStillNotExecuted()
    {
        var store = new Phase79InMemoryPendingActionStore();
        await store.CreateAsync(CreateRequest("pa_ready", "user_a"));
        await store.UpdateStatusAsync(new PendingActionStatusUpdate(
            "pa_ready",
            "user_a",
            PendingActionStatus.ConfirmationRequired,
            PendingActionStatus.Confirmed));

        var result = await store.RecordGuardDecisionReferenceAsync(
            "user_a",
            "pa_ready",
            "guard_allow_execution_ready_fixture",
            PendingActionStatus.ExecutionReady,
            auditEventRef: "audit_guard_ready");

        Assert.True(result.Success);
        Assert.NotNull(result.Record);
        Assert.Equal(PendingActionStatus.ExecutionReady, result.Record.Status);
        Assert.False(result.Record.WroteData);
        Assert.False(result.Record.Executed);
    }

    private static PendingActionCreateRequest CreateRequest(
        string id,
        string user,
        string? idempotencyKeyHash = null,
        DateTimeOffset? expiresAt = null)
    {
        return new PendingActionCreateRequest(
            PendingActionId: id,
            PreviewId: $"preview_{id}",
            ToolId: "mock_phase79_tool",
            ToolVersion: "1.0",
            AdapterId: "mock_phase79_adapter",
            ActionType: "mock_preview_action",
            UserSubjectRef: user,
            SessionSubjectRef: "session_hash_a",
            RiskLevel: "medium_preview_only",
            ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10),
            IdempotencyKeyHash: idempotencyKeyHash ?? $"idem_{id}",
            InputHash: $"input_hash_{id}",
            PreviewHash: $"preview_hash_{id}",
            PolicySnapshotRef: $"policy_{id}",
            TraceId: $"trace_{id}",
            AuditEventRefs: new[] { $"audit_{id}_created" },
            SanitizedPreviewRef: $"preview_ref_{id}",
            ServerOnlyPayloadRef: $"payload_ref_{id}",
            Payload: new Dictionary<string, string> { ["title"] = $"title_{id}" },
            RedactionMetadata: new Dictionary<string, string> { ["status"] = "sanitized" },
            ValidationSnapshot: new Dictionary<string, string> { ["schema"] = "valid" });
    }

    private sealed class Phase79InMemoryPendingActionStore : IPendingActionStore
    {
        private readonly ConcurrentDictionary<string, PendingActionRecord> _records = new();

        public Task<PendingActionStoreResult> CreateAsync(PendingActionCreateRequest request, CancellationToken cancellationToken = default)
        {
            var existing = _records.Values.FirstOrDefault(record =>
                record.UserSubjectRef == request.UserSubjectRef &&
                record.IdempotencyKeyHash == request.IdempotencyKeyHash);
            if (existing is not null)
            {
                return Task.FromResult(PendingActionStoreResult.Succeeded(existing, idempotent: true));
            }

            var now = DateTimeOffset.UtcNow;
            var status = request.ExpiresAt <= now
                ? PendingActionStatus.Expired
                : PendingActionStatus.ConfirmationRequired;
            var record = new PendingActionRecord
            {
                PendingActionId = request.PendingActionId,
                PreviewId = request.PreviewId,
                ToolId = request.ToolId,
                ToolVersion = request.ToolVersion,
                AdapterId = request.AdapterId,
                ActionType = request.ActionType,
                UserSubjectRef = request.UserSubjectRef,
                SessionSubjectRef = request.SessionSubjectRef,
                RiskLevel = request.RiskLevel,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
                ExpiresAt = request.ExpiresAt,
                IdempotencyKeyHash = request.IdempotencyKeyHash,
                InputHash = request.InputHash,
                PreviewHash = request.PreviewHash,
                PolicySnapshotRef = request.PolicySnapshotRef,
                TraceId = request.TraceId,
                AuditEventRefs = request.AuditEventRefs,
                SanitizedPreviewRef = request.SanitizedPreviewRef,
                ServerOnlyPayloadRef = request.ServerOnlyPayloadRef,
                Payload = request.Payload ?? new Dictionary<string, string>(),
                RedactionMetadata = request.RedactionMetadata ?? new Dictionary<string, string>(),
                ValidationSnapshot = request.ValidationSnapshot ?? new Dictionary<string, string>()
            };

            _records[record.PendingActionId] = record;
            return Task.FromResult(PendingActionStoreResult.Succeeded(record));
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
            ExpireDueRecords();
            var results = _records.Values
                .Where(record => record.UserSubjectRef == userSubjectRef)
                .Where(record => PendingActionStatus.IsActive(record.Status))
                .Select(record => record.ToClientView())
                .ToList();
            return Task.FromResult<IReadOnlyList<PendingActionClientView>>(results);
        }

        public Task<PendingActionStoreResult> UpdateStatusAsync(PendingActionStatusUpdate update, CancellationToken cancellationToken = default)
        {
            if (!TryGetOwned(update.UserSubjectRef, update.PendingActionId, out var record))
            {
                return Task.FromResult(PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found."));
            }

            if (record.Status != update.ExpectedStatus)
            {
                return Task.FromResult(PendingActionStoreResult.Failed(record.Status, "status_mismatch", "Pending action status did not match expected status."));
            }

            var updated = Copy(record, update.NewStatus) with
            {
                ConfirmationId = update.ConfirmationId ?? record.ConfirmationId,
                BlockedReason = update.BlockedReason,
                CancellationReason = update.CancellationReason,
                AuditEventRefs = AppendAudit(record, update.AuditEventRef)
            };
            _records[updated.PendingActionId] = updated;
            return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
        }

        public Task<PendingActionStoreResult> MarkExpiredAsync(string userSubjectRef, string pendingActionId, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            return UpdateOwnedStatus(userSubjectRef, pendingActionId, PendingActionStatus.Expired, auditEventRef: auditEventRef);
        }

        public Task<PendingActionStoreResult> CancelAsync(string userSubjectRef, string pendingActionId, string? cancellationReason = null, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            return UpdateOwnedStatus(userSubjectRef, pendingActionId, PendingActionStatus.Cancelled, cancellationReason, auditEventRef);
        }

        public Task<PendingActionStoreResult> RecordConfirmationReferenceAsync(string userSubjectRef, string pendingActionId, string confirmationId, string confirmationRequestHash, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            if (!TryGetOwned(userSubjectRef, pendingActionId, out var record))
            {
                return Task.FromResult(PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found."));
            }

            var updated = Copy(record, record.Status) with
            {
                ConfirmationId = confirmationId,
                ValidationSnapshot = Merge(record.ValidationSnapshot, "confirmationRequestHash", confirmationRequestHash),
                AuditEventRefs = AppendAudit(record, auditEventRef)
            };
            _records[updated.PendingActionId] = updated;
            return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
        }

        public Task<PendingActionStoreResult> RecordGuardDecisionReferenceAsync(string userSubjectRef, string pendingActionId, string guardDecisionRef, string status, string? blockedReason = null, string? auditEventRef = null, CancellationToken cancellationToken = default)
        {
            if (!TryGetOwned(userSubjectRef, pendingActionId, out var record))
            {
                return Task.FromResult(PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found."));
            }

            var updated = Copy(record, status) with
            {
                ValidationSnapshot = Merge(record.ValidationSnapshot, "guardDecisionRef", guardDecisionRef),
                BlockedReason = blockedReason,
                AuditEventRefs = AppendAudit(record, auditEventRef)
            };
            _records[updated.PendingActionId] = updated;
            return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
        }

        public Task<PendingActionStoreResult> CheckIdempotencyKeyHashAsync(string userSubjectRef, string idempotencyKeyHash, CancellationToken cancellationToken = default)
        {
            var existing = _records.Values.FirstOrDefault(record =>
                record.UserSubjectRef == userSubjectRef &&
                record.IdempotencyKeyHash == idempotencyKeyHash);
            return Task.FromResult(existing is null
                ? PendingActionStoreResult.Failed("not_found", "not_found", "Idempotency key was not found.")
                : PendingActionStoreResult.Succeeded(existing, idempotent: true));
        }

        public Task<PendingActionRecord?> GetByPreviewIdAsync(string userSubjectRef, string previewId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.Values.FirstOrDefault(record =>
                record.UserSubjectRef == userSubjectRef &&
                record.PreviewId == previewId));
        }

        public Task<PendingActionRecord?> GetByConfirmationIdAsync(string userSubjectRef, string confirmationId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_records.Values.FirstOrDefault(record =>
                record.UserSubjectRef == userSubjectRef &&
                record.ConfirmationId == confirmationId));
        }

        public Task<IReadOnlyList<PendingActionRecord>> QueryAsync(PendingActionQuery query, CancellationToken cancellationToken = default)
        {
            ExpireDueRecords();
            var records = _records.Values.Where(record => record.UserSubjectRef == query.UserSubjectRef);
            if (query.ActiveOnly)
            {
                records = records.Where(record => PendingActionStatus.IsActive(record.Status));
            }
            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                records = records.Where(record => record.Status == query.Status);
            }
            if (!string.IsNullOrWhiteSpace(query.PreviewId))
            {
                records = records.Where(record => record.PreviewId == query.PreviewId);
            }
            if (!string.IsNullOrWhiteSpace(query.ConfirmationId))
            {
                records = records.Where(record => record.ConfirmationId == query.ConfirmationId);
            }
            if (!string.IsNullOrWhiteSpace(query.TraceId))
            {
                records = records.Where(record => record.TraceId == query.TraceId);
            }
            if (!string.IsNullOrWhiteSpace(query.IdempotencyKeyHash))
            {
                records = records.Where(record => record.IdempotencyKeyHash == query.IdempotencyKeyHash);
            }

            return Task.FromResult<IReadOnlyList<PendingActionRecord>>(records.ToList());
        }

        private Task<PendingActionStoreResult> UpdateOwnedStatus(
            string userSubjectRef,
            string pendingActionId,
            string status,
            string? cancellationReason = null,
            string? auditEventRef = null)
        {
            if (!TryGetOwned(userSubjectRef, pendingActionId, out var record))
            {
                return Task.FromResult(PendingActionStoreResult.Failed("not_found", "not_found", "Pending action was not found."));
            }

            var updated = Copy(record, status) with
            {
                CancellationReason = cancellationReason,
                AuditEventRefs = AppendAudit(record, auditEventRef)
            };
            _records[updated.PendingActionId] = updated;
            return Task.FromResult(PendingActionStoreResult.Succeeded(updated));
        }

        private bool TryGetOwned(string userSubjectRef, string pendingActionId, out PendingActionRecord record)
        {
            if (_records.TryGetValue(pendingActionId, out var found) && found.UserSubjectRef == userSubjectRef)
            {
                record = found;
                return true;
            }

            record = null!;
            return false;
        }

        private void ExpireDueRecords()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var record in _records.Values.Where(record =>
                         PendingActionStatus.IsActive(record.Status) &&
                         record.ExpiresAt <= now))
            {
                _records[record.PendingActionId] = Copy(record, PendingActionStatus.Expired);
            }
        }

        private static PendingActionRecord Copy(PendingActionRecord record, string status)
        {
            return new PendingActionRecord
            {
                PendingActionId = record.PendingActionId,
                PreviewId = record.PreviewId,
                ConfirmationId = record.ConfirmationId,
                ToolId = record.ToolId,
                ToolVersion = record.ToolVersion,
                AdapterId = record.AdapterId,
                ActionType = record.ActionType,
                UserSubjectRef = record.UserSubjectRef,
                SessionSubjectRef = record.SessionSubjectRef,
                RiskLevel = record.RiskLevel,
                Status = status,
                CreatedAt = record.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = record.ExpiresAt,
                IdempotencyKeyHash = record.IdempotencyKeyHash,
                InputHash = record.InputHash,
                PreviewHash = record.PreviewHash,
                PolicySnapshotRef = record.PolicySnapshotRef,
                TraceId = record.TraceId,
                AuditEventRefs = record.AuditEventRefs,
                SanitizedPreviewRef = record.SanitizedPreviewRef,
                ServerOnlyPayloadRef = record.ServerOnlyPayloadRef,
                Payload = record.Payload,
                RedactionMetadata = record.RedactionMetadata,
                ValidationSnapshot = record.ValidationSnapshot,
                BlockedReason = record.BlockedReason,
                CancellationReason = record.CancellationReason,
                SchemaVersion = record.SchemaVersion,
                WroteData = false,
                Executed = false
            };
        }

        private static IReadOnlyList<string> AppendAudit(PendingActionRecord record, string? auditEventRef)
        {
            return string.IsNullOrWhiteSpace(auditEventRef)
                ? record.AuditEventRefs
                : record.AuditEventRefs.Concat(new[] { auditEventRef }).ToList();
        }

        private static IReadOnlyDictionary<string, string> Merge(
            IReadOnlyDictionary<string, string> values,
            string key,
            string value)
        {
            var merged = new Dictionary<string, string>(values)
            {
                [key] = value
            };
            return merged;
        }
    }
}

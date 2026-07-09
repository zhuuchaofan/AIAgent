using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using LifeAgent.Api.Services.Agent.GuardedExecution;
using LifeAgent.Api.Services.Agent.PendingActions;

namespace LifeAgent.Tests;

public class Phase711OfflineGuardChainTest
{
    [Fact]
    public async Task Phase711_FixtureCasesCanBeMappedIntoPendingActionRecords()
    {
        var cases = Cases();

        Assert.Equal(15, cases.Length);
        foreach (var fixtureCase in cases)
        {
            var record = RecordFromFixture(fixtureCase);

            Assert.StartsWith("pa_", record.PendingActionId, StringComparison.Ordinal);
            Assert.StartsWith("preview_", record.PreviewId, StringComparison.Ordinal);
            Assert.StartsWith("payload_ref_", record.ServerOnlyPayloadRef, StringComparison.Ordinal);
            Assert.Equal(fixtureCase.GetString("toolId"), record.ToolId);
            Assert.Equal(fixtureCase.GetString("toolVersion"), record.ToolVersion);
            Assert.Equal(fixtureCase.GetString("adapterId"), record.AdapterId);
            Assert.False(record.WroteData);
            Assert.False(record.Executed);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Phase711_OfflineFixtureGuardChainMatchesSafeDecisionClasses()
    {
        var store = new Phase711InMemoryPendingActionStore();
        var cases = Cases();
        foreach (var fixtureCase in cases)
        {
            await store.SeedAsync(RecordFromFixture(fixtureCase));
        }

        var runtime = new GuardedExecutionRuntime(store);
        foreach (var fixtureCase in cases)
        {
            var response = await runtime.EvaluateExecutionReadinessAsync(RequestFromFixture(fixtureCase));
            var expected = ExpectedDecision(fixtureCase);

            Assert.Contains(response.Decision, expected.AllowedDecisions);
            Assert.Equal(expected.Success, response.Success);
            Assert.Equal(expected.ExecutionReady, response.ExecutionReady);
            Assert.False(response.Executed);
            Assert.False(response.WroteData);
            Assert.False(response.ExternalCallMade);
        }
    }

    [Fact]
    public async Task Phase711_LowRiskAndFutureFixtureOnlyCasesCanReachReadinessOnly()
    {
        var cases = Cases().ToDictionary(item => item.GetString("fixtureId"));
        var store = new Phase711InMemoryPendingActionStore();
        await store.SeedAsync(RecordFromFixture(cases["case_01_low_risk_readonly_preview_allowed"]));
        await store.SeedAsync(RecordFromFixture(cases["case_15_execution_ready_future_gated_not_executed"]));
        var runtime = new GuardedExecutionRuntime(store);

        var lowRisk = await runtime.EvaluateExecutionReadinessAsync(RequestFromFixture(cases["case_01_low_risk_readonly_preview_allowed"]));
        var futureReady = await runtime.EvaluateExecutionReadinessAsync(RequestFromFixture(cases["case_15_execution_ready_future_gated_not_executed"]));

        Assert.Equal(GuardDecisionType.AllowExecutionReady, lowRisk.Decision);
        Assert.Equal(PendingActionStatus.ExecutionReady, lowRisk.Status);
        Assert.True(lowRisk.ExecutionReady);
        Assert.False(lowRisk.Executed);

        Assert.Equal(GuardDecisionType.AllowExecutionReady, futureReady.Decision);
        Assert.Equal(PendingActionStatus.ExecutionReady, futureReady.Status);
        Assert.True(futureReady.ExecutionReady);
        Assert.False(futureReady.Executed);
    }

    [Fact]
    public async Task Phase711_WriteExternalHighRiskAndLifecycleCasesStayBlockedOffline()
    {
        var cases = Cases().ToDictionary(item => item.GetString("fixtureId"));
        var selectedIds = new[]
        {
            "case_02_memory_write_preview_release_gate_missing",
            "case_03_expired_pending_action_confirmation_rejected",
            "case_04_cross_user_confirmation_blocked",
            "case_05_stale_tool_version_blocked",
            "case_06_input_hash_mismatch_blocked",
            "case_07_preview_hash_mismatch_blocked",
            "case_09_external_call_requested_blocked",
            "case_10_high_risk_requires_release_gate",
            "case_13_cancelled_action_cannot_execute",
            "case_14_confirmed_action_still_not_executed"
        };
        var store = new Phase711InMemoryPendingActionStore();
        foreach (var id in selectedIds)
        {
            await store.SeedAsync(RecordFromFixture(cases[id]));
        }

        var runtime = new GuardedExecutionRuntime(store);
        foreach (var id in selectedIds)
        {
            var response = await runtime.EvaluateExecutionReadinessAsync(RequestFromFixture(cases[id]));

            Assert.False(response.Success);
            Assert.False(response.ExecutionReady);
            Assert.False(response.Executed);
            Assert.False(response.WroteData);
            Assert.DoesNotContain("secret", response.BlockedReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", response.BlockedReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw", response.BlockedReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Phase711_GuardDecisionRecordingKeepsStoreNoExecution()
    {
        var fixtureCase = Cases().Single(item => item.GetString("fixtureId") == "case_02_memory_write_preview_release_gate_missing");
        var store = new Phase711InMemoryPendingActionStore();
        var record = RecordFromFixture(fixtureCase);
        await store.SeedAsync(record);
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(RequestFromFixture(fixtureCase));
        var stored = await store.GetByIdAsync(record.UserSubjectRef, record.PendingActionId);

        Assert.False(response.Success);
        Assert.NotNull(stored);
        Assert.Equal(PendingActionStatus.ExecutionBlocked, stored.Status);
        Assert.Equal("write_intent_blocked", stored.BlockedReason);
        Assert.Contains("guardDecisionRef", stored.ValidationSnapshot.Keys);
        Assert.Contains(stored.AuditEventRefs, item => item.Contains("execution_blocked", StringComparison.Ordinal));
        Assert.False(stored.WroteData);
        Assert.False(stored.Executed);
    }

    [Fact]
    public async Task Phase711_ResponseSurfaceDoesNotExposeServerOnlyFixtureData()
    {
        var fixtureCase = Cases().Single(item => item.GetString("fixtureId") == "case_11_secret_like_value_redacted");
        var store = new Phase711InMemoryPendingActionStore();
        await store.SeedAsync(RecordFromFixture(fixtureCase));
        var runtime = new GuardedExecutionRuntime(store);

        var response = await runtime.EvaluateExecutionReadinessAsync(RequestFromFixture(fixtureCase));
        var responseJson = JsonSerializer.Serialize(response);

        Assert.DoesNotContain("payload_ref", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fake_api_key_should_be_redacted", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPrompt", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fullContext", responseJson, StringComparison.OrdinalIgnoreCase);
        Assert.False(response.Executed);
        Assert.False(response.WroteData);
    }

    private static GuardedExecutionRequest RequestFromFixture(JsonElement fixtureCase)
    {
        var id = fixtureCase.GetString("fixtureId");
        var user = fixtureCase.GetProperty("confirmationRequest").GetProperty("sameUser").GetBoolean()
            ? "user_a"
            : "user_b";
        var requestedToolVersion = id == "case_05_stale_tool_version_blocked"
            ? "1.0"
            : fixtureCase.GetString("toolVersion");
        var inputHash = id == "case_06_input_hash_mismatch_blocked"
            ? $"bad_input_{id}"
            : $"input_{id}";
        var previewHash = id == "case_07_preview_hash_mismatch_blocked"
            ? $"bad_preview_{id}"
            : $"preview_{id}";

        return new GuardedExecutionRequest(
            UserSubjectRef: user,
            PendingActionId: $"pa_{id}",
            ConfirmationId: $"confirm_pa_{id}",
            ToolId: fixtureCase.GetString("toolId"),
            ToolVersion: requestedToolVersion,
            InputHash: inputHash,
            PreviewHash: previewHash,
            ConfirmationHash: $"confirmation_pa_{id}",
            IdempotencyKeyHash: $"idem_pa_{id}",
            WriteIntent: fixtureCase.GetProperty("writeIntent").GetBoolean(),
            ExternalCall: fixtureCase.GetProperty("externalCall").GetBoolean(),
            RiskLevel: fixtureCase.GetString("riskLevel"),
            TraceId: $"trace_pa_{id}");
    }

    private static PendingActionRecord RecordFromFixture(JsonElement fixtureCase)
    {
        var id = fixtureCase.GetString("fixtureId");
        var pendingId = $"pa_{id}";
        var status = fixtureCase.GetProperty("pendingActionExpected").GetString("status") switch
        {
            PendingActionStatus.Expired => PendingActionStatus.Expired,
            PendingActionStatus.Cancelled => PendingActionStatus.Cancelled,
            PendingActionStatus.Confirmed => PendingActionStatus.Confirmed,
            _ => PendingActionStatus.Confirmed
        };
        var expiresAt = fixtureCase.GetString("ttlExpirationBehavior") == "expired"
            ? DateTimeOffset.UtcNow.AddMinutes(-10)
            : DateTimeOffset.UtcNow.AddMinutes(10);

        return new PendingActionRecord
        {
            PendingActionId = pendingId,
            PreviewId = $"preview_{pendingId}",
            ConfirmationId = $"confirm_{pendingId}",
            ToolId = fixtureCase.GetString("toolId"),
            ToolVersion = fixtureCase.GetString("toolVersion"),
            AdapterId = fixtureCase.GetString("adapterId"),
            ActionType = $"offline_{fixtureCase.GetProperty("inputPayload").GetString("intent")}",
            UserSubjectRef = "user_a",
            SessionSubjectRef = "session_a",
            RiskLevel = fixtureCase.GetString("riskLevel"),
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = expiresAt,
            IdempotencyKeyHash = $"idem_{pendingId}",
            InputHash = $"input_{id}",
            PreviewHash = $"preview_{id}",
            PolicySnapshotRef = $"policy_{id}",
            TraceId = $"trace_{pendingId}",
            AuditEventRefs = new[] { $"audit_{pendingId}_confirmed" },
            SanitizedPreviewRef = $"safe_preview_{id}",
            ServerOnlyPayloadRef = $"payload_ref_{id}",
            RedactionMetadata = new Dictionary<string, string>
            {
                ["status"] = "sanitized"
            },
            ValidationSnapshot = new Dictionary<string, string>
            {
                ["confirmationHash"] = $"confirmation_{pendingId}",
                ["fixtureId"] = id
            },
            WroteData = false,
            Executed = false
        };
    }

    private static ExpectedDecisionClass ExpectedDecision(JsonElement fixtureCase)
    {
        var id = fixtureCase.GetString("fixtureId");
        return id switch
        {
            "case_01_low_risk_readonly_preview_allowed" => new ExpectedDecisionClass(true, true, new[] { GuardDecisionType.AllowExecutionReady }),
            "case_03_expired_pending_action_confirmation_rejected" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectExpired }),
            "case_04_cross_user_confirmation_blocked" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectCrossUser }),
            "case_05_stale_tool_version_blocked" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectToolVersionMismatch }),
            "case_06_input_hash_mismatch_blocked" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectHashMismatch }),
            "case_07_preview_hash_mismatch_blocked" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectHashMismatch }),
            "case_09_external_call_requested_blocked" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectExternalCall }),
            "case_13_cancelled_action_cannot_execute" => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectCancelled }),
            "case_15_execution_ready_future_gated_not_executed" => new ExpectedDecisionClass(true, true, new[] { GuardDecisionType.AllowExecutionReady }),
            _ when fixtureCase.GetProperty("writeIntent").GetBoolean() => new ExpectedDecisionClass(false, false, new[] { GuardDecisionType.RejectWriteIntent }),
            _ => new ExpectedDecisionClass(true, true, new[] { GuardDecisionType.AllowExecutionReady })
        };
    }

    private static JsonElement[] Cases()
    {
        using var document = LoadFixture();
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    private static JsonDocument LoadFixture([CallerFilePath] string sourceFile = "")
    {
        var testDir = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("Could not resolve test source directory.");
        var fixturePath = Path.Combine(testDir, "Fixtures", "Phase7_8", "offline_mock_tool_regression.json");
        return JsonDocument.Parse(File.ReadAllText(fixturePath));
    }

    private sealed record ExpectedDecisionClass(
        bool Success,
        bool ExecutionReady,
        IReadOnlyCollection<GuardDecisionType> AllowedDecisions);

    private sealed class Phase711InMemoryPendingActionStore : IPendingActionStore
    {
        private readonly ConcurrentDictionary<string, PendingActionRecord> _records = new();

        public Task SeedAsync(PendingActionRecord record)
        {
            _records[record.PendingActionId] = record;
            return Task.CompletedTask;
        }

        public Task<PendingActionStoreResult> CreateAsync(PendingActionCreateRequest request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Phase 7.11 seeds offline fixture records directly.");
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
            throw new NotSupportedException("Phase 7.11 offline guard chain uses guard decision updates only.");
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
            throw new NotSupportedException("Phase 7.11 seeds confirmation refs from offline fixtures.");
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
            var records = _records.Values.Where(record => record.UserSubjectRef == query.UserSubjectRef);
            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                records = records.Where(record => record.Status == query.Status);
            }

            return Task.FromResult<IReadOnlyList<PendingActionRecord>>(records.ToList());
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

internal static class Phase711JsonElementExtensions
{
    public static string GetString(this JsonElement element, string propertyName)
    {
        return element.GetProperty(propertyName).GetString()
            ?? throw new InvalidOperationException($"Fixture property '{propertyName}' is required.");
    }
}

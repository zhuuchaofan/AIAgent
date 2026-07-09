using System.Runtime.CompilerServices;
using System.Text.Json;

namespace LifeAgent.Tests;

public class Phase78OfflineMockRegressionTest
{
    private static readonly string[] RequiredFixtureIds =
    {
        "case_01_low_risk_readonly_preview_allowed",
        "case_02_memory_write_preview_release_gate_missing",
        "case_03_expired_pending_action_confirmation_rejected",
        "case_04_cross_user_confirmation_blocked",
        "case_05_stale_tool_version_blocked",
        "case_06_input_hash_mismatch_blocked",
        "case_07_preview_hash_mismatch_blocked",
        "case_08_duplicate_confirmation_idempotent",
        "case_09_external_call_requested_blocked",
        "case_10_high_risk_requires_release_gate",
        "case_11_secret_like_value_redacted",
        "case_12_raw_prompt_full_context_prohibited",
        "case_13_cancelled_action_cannot_execute",
        "case_14_confirmed_action_still_not_executed",
        "case_15_execution_ready_future_gated_not_executed"
    };

    [Fact]
    public void Phase78_FixtureDefinesRequiredMockCaseMatrix()
    {
        using var document = LoadFixture();
        var root = document.RootElement;

        Assert.Equal("phase7.8.offline_fixture.v1", root.GetProperty("schemaVersion").GetString());
        var cases = root.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(RequiredFixtureIds.Length, cases.Length);

        var ids = cases.Select(item => item.GetProperty("fixtureId").GetString()).ToHashSet();
        foreach (var requiredId in RequiredFixtureIds)
        {
            Assert.Contains(requiredId, ids);
        }
    }

    [Fact]
    public void Phase78_ClientVisiblePreviewAndResultsNeverClaimExecution()
    {
        foreach (var item in Cases())
        {
            var preview = item.GetProperty("sanitizedPreviewExpected");
            var confirmation = item.GetProperty("confirmationExpectedResult");
            var guard = item.GetProperty("guardDecisionExpected");

            Assert.False(preview.GetProperty("claimsExecuted").GetBoolean());
            Assert.False(confirmation.GetProperty("wroteData").GetBoolean());
            Assert.False(confirmation.GetProperty("executed").GetBoolean());
            Assert.False(guard.GetProperty("executed").GetBoolean());
        }
    }

    [Fact]
    public void Phase78_ServerOnlyPayloadIsReferenceAndNeverClientAuthority()
    {
        foreach (var item in Cases())
        {
            var shape = item.GetProperty("serverOnlyPayloadExpectedShape");
            var pending = item.GetProperty("pendingActionExpected");

            Assert.True(shape.GetProperty("storedAsReference").GetBoolean());
            Assert.True(pending.GetProperty("serverSideOnly").GetBoolean());
        }
    }

    [Fact]
    public void Phase78_UnsafeExecutionDefaultsAreBlocked()
    {
        foreach (var item in Cases())
        {
            var releaseGateState = item.GetProperty("releaseGateState").GetString();
            var writeIntent = item.GetProperty("writeIntent").GetBoolean();
            var externalCall = item.GetProperty("externalCall").GetBoolean();
            var riskLevel = item.GetProperty("riskLevel").GetString();
            var guard = item.GetProperty("guardDecisionExpected");
            var decision = guard.GetProperty("decision").GetString();

            if (releaseGateState == "missing" && (writeIntent || externalCall || riskLevel is "high_internal_write" or "high_external_side_effect" or "critical_release_gated"))
            {
                Assert.Contains(
                    decision,
                    new[]
                    {
                        "allow_confirmation_only",
                        "require_release_gate",
                        "reject_external_call",
                        "reject_write_intent",
                        "block_execution",
                        "reject_expired",
                        "reject_cross_user",
                        "reject_stale_action",
                        "reject_policy_mismatch"
                    });
                Assert.False(guard.GetProperty("executionReady").GetBoolean());
            }
        }
    }

    [Fact]
    public void Phase78_BlockedAndRejectedCasesUseSanitizedReasons()
    {
        foreach (var item in Cases())
        {
            var reason = item.GetProperty("blockedReasonExpected");
            if (reason.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var reasonText = reason.GetString();
            Assert.False(string.IsNullOrWhiteSpace(reasonText));
            Assert.DoesNotContain("secret", reasonText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("token", reasonText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("raw", reasonText, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Phase78_TraceAuditSurfaceContainsExpectedEventsOnlyAsSafeNames()
    {
        foreach (var item in Cases())
        {
            var events = item.GetProperty("traceAuditExpectedEvents").EnumerateArray().Select(value => value.GetString()).ToArray();

            Assert.NotEmpty(events);
            Assert.All(events, eventName =>
            {
                Assert.False(string.IsNullOrWhiteSpace(eventName));
                Assert.DoesNotContain("secret", eventName!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("token", eventName!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("prompt", eventName!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("context", eventName!, StringComparison.OrdinalIgnoreCase);
            });
        }
    }

    [Fact]
    public void Phase78_ProhibitedValuesAreAbsentFromClientTraceAndAuditSurfaces()
    {
        foreach (var item in Cases())
        {
            var prohibited = item.GetProperty("prohibitedFieldsExpectedAbsent")
                .EnumerateArray()
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            var inspectedSurface = JsonSerializer.Serialize(new
            {
                sanitizedPreviewExpected = item.GetProperty("sanitizedPreviewExpected"),
                pendingActionExpected = item.GetProperty("pendingActionExpected"),
                confirmationExpectedResult = item.GetProperty("confirmationExpectedResult"),
                guardDecisionExpected = item.GetProperty("guardDecisionExpected"),
                traceAuditExpectedEvents = item.GetProperty("traceAuditExpectedEvents"),
                blockedReasonExpected = item.GetProperty("blockedReasonExpected")
            });

            foreach (var prohibitedValue in prohibited)
            {
                Assert.DoesNotContain(prohibitedValue!, inspectedSurface, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Phase78_SpecificLifecycleGuardsMatchContract()
    {
        var byId = Cases().ToDictionary(item => item.GetProperty("fixtureId").GetString()!);

        Assert.Equal("expired", byId["case_03_expired_pending_action_confirmation_rejected"].GetProperty("confirmationExpectedResult").GetProperty("status").GetString());
        Assert.Equal("not_found", byId["case_04_cross_user_confirmation_blocked"].GetProperty("confirmationExpectedResult").GetProperty("status").GetString());
        Assert.Equal("confirmation_blocked", byId["case_05_stale_tool_version_blocked"].GetProperty("confirmationExpectedResult").GetProperty("status").GetString());
        Assert.True(byId["case_08_duplicate_confirmation_idempotent"].GetProperty("confirmationExpectedResult").GetProperty("idempotent").GetBoolean());
        Assert.Equal("cancelled", byId["case_13_cancelled_action_cannot_execute"].GetProperty("confirmationExpectedResult").GetProperty("status").GetString());
        Assert.False(byId["case_14_confirmed_action_still_not_executed"].GetProperty("confirmationExpectedResult").GetProperty("executed").GetBoolean());
        Assert.True(byId["case_15_execution_ready_future_gated_not_executed"].GetProperty("guardDecisionExpected").GetProperty("executionReady").GetBoolean());
        Assert.False(byId["case_15_execution_ready_future_gated_not_executed"].GetProperty("guardDecisionExpected").GetProperty("executed").GetBoolean());
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
}

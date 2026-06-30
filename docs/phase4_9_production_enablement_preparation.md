# Phase 4.9 - Production Enablement Preparation

## Current Baseline

Phase 4.8 has completed the `create_life_event` real write code path.

What exists now:

- `/api/agent/confirm` supports both preview-only and flags-true write branches.
- `AgentLifeEventConfirmationWriteCoordinator` implements idempotent write coordination.
- `FirestoreAgentLifeEventWriter` writes to `users/{userId}/life_events/{eventId}`.
- `IAgentLifeEventService` orchestrates the writer with the feature gate.
- Event ID is derived: `eventId = evt_{agentActionId}`.
- Duplicate confirm returns the same `createdResourceId` without re-writing.
- Write failure returns `write_failed`, pending action stays `pending`, `wroteData=false`.
- Invalid / cancelled / expired / cross-user actions do not write.
- DI safety tests validate singleton/scoped lifecycle boundaries.
- Coordinator unit tests, confirm endpoint tests, payload mapper tests, and skeleton tests all pass.

Default production behavior:

- `ENABLE_AGENT_WRITE_TOOLS` is **not set** → defaults to `false`.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is **not set** → defaults to `false`.
- `AgentWriteFeatureGate.CanCreateLifeEvent()` returns `false`.
- `/api/agent/confirm` is preview-only for `create_life_event`.
- No `life_event` document is created in Firestore.
- `previewOnly=true`, `wroteData=false`.

What has **not** been done:

- Not deployed to production.
- Not pushed to remote.
- Cloud Run env not modified.
- Firestore Rules not modified.
- Firebase Auth not modified.
- No frontend UI to display / edit / delete agent-created life events.
- No production smoke test.
- No rollback or cleanup procedure defined.
- No staging or canary environment configured.
- No observability or log audit plan.
- No grayscale / canary rollout plan.

Reference document: `docs/phase4_8_create_life_event_closeout.md`.

## Project Context

LifeAgent is a personal AI Agent application. The project goal is not limited to chat — it builds an **agent action write pipeline**:

```
AI proposes action
    → pending action (safety buffer)
        → user confirms
            → feature gate check
                → safe write coordinator
                    → real domain write
                        → idempotent result
```

The `pending action` layer is the safety buffer between AI proposal and real data mutation. The `/api/agent/confirm` endpoint is the user-authorized write entry point. The `AgentWriteFeatureGate` is the production safety switch.

`create_life_event` is the **first** Agent write MVP action type. It is the proving ground for the write pipeline pattern. If this pattern is safe and correct, the same coordinator/gate/idempotency model will be reused for future action types.

Future action types that may follow (not in Phase 4.9):

- `update_life_event`
- `delete_life_event`
- `create_reminder`
- `update_memory`
- `write_calendar_event`
- MCP tool execution

## Goal

Phase 4.9 prepares everything needed to **safely enable** `create_life_event` real writes in production.

Phase 4.9 does **not** open production writes. It answers:

- What must be verified before enabling.
- What Firestore Rules are needed.
- How to configure and control the feature flag.
- How to smoke test without breaking production.
- How to roll back if something goes wrong.
- Whether the frontend can handle the new data.
- What logs and observability are needed.
- Where Phase 4.9 ends and Phase 5 begins.

## Non-Goals

Phase 4.9 does **not**:

- Enable production real writes for `create_life_event`.
- Deploy to production Cloud Run.
- Open the Cloud Run feature flag.
- Modify Firestore Rules in production.
- Build a frontend UI for viewing / editing / deleting agent-created life events.
- Extend to `reminder`, `memory`, `calendar`, `MCP`, or any other action type.
- Change the confirm semantics established in Phase 4.8.
- Add new tests to the coordinator or confirm endpoint (Phase 4.8 is complete).
- Set up a staging environment (out of scope for this phase; may be revisited).
- Implement user-facing rollback controls.

## Why This Phase Exists

Phase 4.8 completed the **code path**. The code is tested and documented. But the code path alone is not enough to safely enable production writes. Between "code works in tests" and "production is safe to enable", there are gaps:

1. **Feature flag control**: The flag names exist, but there is no documented procedure for setting, verifying, or rolling back the Cloud Run env vars.
2. **Firestore Rules**: The current rules may not cover `life_events` write paths for the service account. Without explicit rules, enabling writes could fail or, worse, create unprotected data.
3. **User boundary**: The backend enforces `userId` from `FirebaseAuthMiddleware`, but this has not been verified end-to-end in a production-like environment.
4. **Smoke test**: There is no manual or scripted smoke test for the production write path.
5. **Frontend gap**: If backend writes succeed but the frontend does not display agent-created life events, users see no feedback — a broken experience.
6. **Observability**: There is no log audit plan for monitoring write activity, failures, or anomalies.
7. **Rollback**: If something goes wrong after enabling, there is no defined procedure to safely disable and clean up.

Phase 4.9 exists to close these gaps **before** any production change is made.

## Boundary With Phase 4.8

| Dimension | Phase 4.8 | Phase 4.9 |
|---|---|---|
| Code | Implements write coordinator, feature gate, confirm integration | Does not modify code |
| Tests | Adds coordinator, DI safety, endpoint tests | Does not add tests (Phase 4.8 is sufficient) |
| Flags | Defines flag names and gate logic | Documents how to set / verify / rollback flags in Cloud Run |
| Firestore | Writes to `users/{userId}/life_events/{eventId}` path | Documents Firestore Rules needed for that path |
| Auth | Enforces userId from `FirebaseAuthMiddleware` | Documents how to verify user boundary in production |
| Deploy | Does not deploy | Does not deploy |
| Frontend | Does not touch frontend | Reviews frontend readiness, does not implement UI |
| Production | Does not enable production writes | Does not enable production writes |

Phase 4.8 answers: "Is the write path correct and tested?"
Phase 4.9 answers: "Is production ready to safely enable the write path?"

## Boundary With Phase 5

| Dimension | Phase 4.9 | Phase 5 |
|---|---|---|
| Enable writes | Prepares but does not enable | May enable (grayscale or full) |
| Frontend | Reviews readiness only | Implements life event display / edit / delete UI |
| Smoke test | Defines the test plan | Executes the test plan in production |
| Rollback | Documents rollback procedure | Executes rollback if needed |
| New action types | None | May introduce `update_life_event`, `delete_life_event` |
| Memory / Reminder / Calendar / MCP | None | May begin exploration |
| Staging environment | Out of scope | May set up if needed |

Phase 4.9 is the **planning layer** between code-complete (4.8) and production-safe (5.x).

## Production Enablement Checklist

### 1. Feature Flag / Cloud Run Env

**Flag names:**

| Env Var | Purpose | Default |
|---|---|---|
| `ENABLE_AGENT_WRITE_TOOLS` | Master switch for all agent write tools | Not set (evaluates to `false`) |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | Switch for `create_life_event` write path | Not set (evaluates to `false`) |

Both must be `true` for `AgentWriteFeatureGate.CanCreateLifeEvent()` to return `true`. The check is:

```csharp
CanCreateLifeEvent => EnableAgentWriteTools && EnableCreateLifeEventTool;
```

The `IsTrue` helper accepts `"true"`, `"1"`, or `"yes"` (case-insensitive). Anything else evaluates to `false`.

`AgentWriteFeatureGate` is registered as a **singleton**. Values are read once from `IConfiguration` at startup and cached for the process lifetime. Changing the Cloud Run env var requires a **new revision deploy** (or traffic migration) to take effect.

**Checklist items before enabling:**

- [ ] Confirm both flag names match the code in `AgentWriteFeatureOptions`.
- [ ] Confirm the Cloud Run service name and region.
- [ ] Document how to set env vars via `gcloud run services update --set-env-vars`.
- [ ] Document how to verify current env vars via `gcloud run services describe`.
- [ ] Document how to unset env vars (revert to default `false`).
- [ ] Confirm that a new revision is required for env var changes to take effect.
- [ ] Confirm that a rollback revision (with flags unset) is ready to receive traffic.
- [ ] Confirm no other env var can accidentally enable writes.
- [ ] Confirm that service registration in `Program.cs` does not bypass the feature gate.

**Important**: Service registration (`builder.Services.AddSingleton<IAgentWriteFeatureGate, ...>`) does **not** mean production writing is enabled. The gate is checked at runtime. Even with all services registered, `CanCreateLifeEvent()` returns `false` unless both env vars are set.

### 2. Firestore Rules

The Firestore project is `copper-affinity-467409-k7`. The data path for agent-created life events is:

```
users/{userId}/life_events/{eventId}
```

This is the same path used by the existing `LifeEventService` (manual CRUD). The backend writes via the Firebase Admin SDK (service account), which **bypasses Firestore Rules by default**. However, Firestore Rules are still needed if:

- The frontend ever reads `life_events` directly from the client SDK.
- The rules need to enforce that only the owning user's data is accessible.
- A future change switches from Admin SDK to client-side writes.

**Checklist items:**

- [ ] Review current `firestore.rules` for `life_events` coverage.
- [ ] Confirm whether the frontend currently reads `life_events` from the client SDK or only through the backend API.
- [ ] If frontend reads via client SDK: draft rules that allow `read` only for `request.auth.uid == userId`.
- [ ] If frontend reads only via backend: document that Firestore Rules are not a blocker for the write path (Admin SDK bypasses rules), but still draft rules for defense-in-depth.
- [ ] Confirm that the backend service account is the only entity that writes to `life_events`.
- [ ] Document the cross-project Auth constraint: Firestore is in `copper-affinity-467409-k7` but Firebase Auth is in `my-agent-app-a5e42`. `request.auth` in Firestore Rules only validates tokens from the same project.
- [ ] Draft rules that deny cross-user read and write at the rules level.
- [ ] Do **not** deploy rules changes as part of Phase 4.9.

**Proposed rules draft (for Phase 4.9.2):**

```javascript
// users/{userId}/life_events/{eventId}
match /users/{userId}/life_events/{eventId} {
  // Only the owning user can read their life events.
  // Requires Firebase Auth enabled on the Firestore project,
  // or unified project auth (see cross-project constraint).
  allow read: if request.auth != null && request.auth.uid == userId;

  // Writes are backend-only (Admin SDK bypasses rules).
  // Explicitly deny client-side writes as defense-in-depth.
  allow write: if false;
}
```

This rules draft assumes:

- Frontend reads through the backend API, not the client SDK directly.
- All writes go through the backend using the Admin SDK.
- If frontend direct reads are needed later, the `read` rule above covers it (with the cross-project Auth prerequisite).

### 3. Firebase Auth And User Boundary

The confirm flow extracts `userId` from `HttpContext.Items["userId"]`, set by `FirebaseAuthMiddleware`. The backend never trusts a `userId` from the request body.

**Cross-user protection layers:**

| Layer | Location | Check |
|---|---|---|
| Middleware | `FirebaseAuthMiddleware` | Extracts `userId` from verified Firebase token |
| Endpoint | `AgentEndpoints.cs` | Returns 401 if `userId` is null or empty |
| Store GetAsync | `FirestorePendingAgentActionStore.cs` | Returns `null` if stored `UserId != authenticated userId` |
| Store ConfirmAsync | `FirestorePendingAgentActionStore.cs` | Returns `Failed("not_found")` if `UserId != authenticated userId` inside transaction |
| Store ConfirmWriteCompletedAsync | `FirestorePendingAgentActionStore.cs` | Returns `Failed("not_found")` if `UserId != authenticated userId` inside transaction |
| Writer | `FirestoreAgentLifeEventWriter.cs` | Writes to `users/{authenticatedUserId}/life_events/{eventId}` — path is user-scoped |

**Checklist items:**

- [ ] Confirm `FirebaseAuthMiddleware` correctly rejects expired / invalid tokens.
- [ ] Confirm pending action document path is `users/{userId}/agent_pending_actions/{actionId}`.
- [ ] Confirm life event document path is `users/{userId}/life_events/{eventId}`.
- [ ] Confirm that User A cannot confirm User B's pending action (covered by tests, but verify in smoke test).
- [ ] Confirm that the backend does not accept `userId` from the request body for any write path.
- [ ] Confirm that the `eventId` derivation (`evt_{agentActionId}`) does not leak across users (it does not, because the write path is scoped to `authenticatedUserId`).

### 4. Backend Smoke Test Plan

Smoke tests should be executed **after** enabling the flag on a test user, and **before** enabling for all users.

**Smoke test checklist:**

| # | Scenario | Expected Result | Pass/Fail |
|---|---|---|---|
| 1 | Flags both `false`, confirm `create_life_event` | `previewOnly=true`, `wroteData=false`, no Firestore write | |
| 2 | Flags both `true`, test user confirms valid `create_life_event` | `wroteData=true`, `createdResourceType=life_event`, `createdResourceId=evt_{actionId}`, Firestore document exists | |
| 3 | Duplicate confirm on same action (scenario 2) | `wroteData=true`, `idempotent=true`, same `createdResourceId`, no duplicate Firestore document | |
| 4 | Flags `true`, invalid payload (missing required fields) | `invalid_payload`, no write | |
| 5 | Flags `true`, cancelled action | `cancelled`, no write | |
| 6 | Flags `true`, expired action | `expired`, no write | |
| 7 | Flags `true`, User A tries to confirm User B's action | `not_found`, no write | |
| 8 | Set only `ENABLE_AGENT_WRITE_TOOLS=true`, leave other `false` | `previewOnly=true`, `wroteData=false` (both flags required) | |
| 9 | Set only `ENABLE_CREATE_LIFE_EVENT_TOOL=true`, leave other `false` | `previewOnly=true`, `wroteData=false` (both flags required) | |
| 10 | After toggling flags back to `false`, confirm a new action | `previewOnly=true`, `wroteData=false` | |

**Verification commands:**

```bash
# Check Cloud Run env vars
gcloud run services describe <SERVICE_NAME> --region <REGION> --format "value(spec.template.spec.containers[0].env)"

# Verify Firestore document exists
# (via Firebase console or gcloud firestore operations)

# Verify no unexpected life_events were created
# Query users/{testUserId}/life_events/ and confirm count matches expected
```

### 5. Frontend Readiness

**Current state**: The frontend has **no UI** to display agent-created life events. The life events tab (`生活助理`) shows user-ingested events from the `/api/life/ingest` path, but does not differentiate or display agent-created events.

**Impact assessment:**

- If the backend starts writing `life_event` documents, they will appear in the existing `GET /api/life/events` list (same collection: `users/{userId}/life_events/`).
- The frontend timeline may display agent-created events with `source=agent` alongside user-created events.
- Agent-created events may have different field structures (e.g., `eventType`, `tags`, `happenedAt` mapped from the LLM proposal) compared to user-ingested events.
- If the frontend does not handle the `source` field or unexpected field shapes, it may display raw / broken data.

**Checklist items:**

- [ ] Confirm whether `GET /api/life/events` returns agent-created events (same collection → yes).
- [ ] Confirm the frontend timeline component handles events with `source=agent`.
- [ ] Confirm the frontend does not crash on unexpected field structures.
- [ ] Decide: is it acceptable for agent-created events to appear in the timeline without special treatment? (For Phase 4.9, likely yes — the event is valid data.)
- [ ] If not acceptable: the minimum fix is to filter or tag agent-created events, **not** to build a full UI. This would be a small frontend change in Phase 5.
- [ ] Document that full life event management UI (edit, delete, approve/reject display) is Phase 5 scope.

**Recommendation**: For the initial smoke test with a test user, frontend readiness is not a blocker. Agent-created events appearing in the timeline is acceptable — they are real data. A Phase 5 enhancement can add source badges, edit capability, or agent-specific display treatment.

### 6. Observability And Logs

When the write path is enabled, the following should be observable in Cloud Run logs:

**Required log signals:**

| Signal | Source | Content |
|---|---|---|
| Confirm request received | Endpoint | `actionId`, `userId`, `actionType`, timestamp |
| Feature gate result | Coordinator / Endpoint | Whether `CanCreateLifeEvent()` returned `true` or `false` |
| Write initiated | Coordinator | `actionId`, `eventId`, `userId` |
| Write succeeded | Coordinator / Writer | `eventId`, `userId`, `createdResourceId` |
| Write failed | Coordinator / Writer | `eventId`, `userId`, error message |
| Duplicate confirm | Coordinator | `actionId`, `userId`, `idempotent=true` |
| Cross-user reject | Store | `actionId`, authenticated `userId`, stored `userId` |
| Payload validation fail | Validator | `actionId`, validation error details |

**Checklist items:**

- [ ] Confirm that the existing code logs the signals above (or add minimal logging if missing).
- [ ] Confirm that sensitive payload content (full `payload` JSON) is **not** logged.
- [ ] Confirm that `userId` and `actionId` are logged for traceability.
- [ ] Document how to query Cloud Run logs for: write successes, write failures, duplicate confirms, cross-user rejects.
- [ ] Document log-based alerting thresholds (optional, can be Phase 5).

### 7. Rollback Plan

**Minimal rollback**: Turn off the feature flag.

Rollback procedure:

1. Update Cloud Run env to unset or set to `false`:
   ```bash
   gcloud run services update <SERVICE_NAME> \
     --region <REGION> \
     --remove-env-vars ENABLE_AGENT_WRITE_TOOLS,ENABLE_CREATE_LIFE_EVENT_TOOL
   ```
   Or explicitly set to `false`:
   ```bash
   gcloud run services update <SERVICE_NAME> \
     --region <REGION> \
     --set-env-vars ENABLE_AGENT_WRITE_TOOLS=false,ENABLE_CREATE_LIFE_EVENT_TOOL=false
   ```

2. This creates a new revision. Traffic migrates automatically (or manually if using pinned traffic).

3. After rollback: `/api/agent/confirm` returns to preview-only. No new `life_event` documents are created.

**What rollback does NOT do:**

- It does **not** delete `life_event` documents already written.
- It does **not** revert pending actions that were already confirmed and written.
- It does **not** affect existing user data.

**Post-rollback behavior:**

- New confirm requests → `previewOnly=true`, `wroteData=false`.
- Duplicate confirm on a previously-written action → `previewOnly=true`, `wroteData=false` (the `CanCreateLifeEvent()` gate fails, so the coordinator is not entered; the store marks it as confirmed via the preview path).
- Pending actions that were in `pending` state (write failed earlier) → remain `pending`, can be retried when flag is re-enabled.

**Firestore Rules rollback:**

If Firestore Rules changes cause issues:

```bash
# Revert to previous rules version
firebase deploy --only firestore:rules
```

Keep the previous `firestore.rules` version in git for easy revert.

**Checklist items:**

- [ ] Confirm the Cloud Run revision with flags `false` is deployable.
- [ ] Document the exact `gcloud` commands for flag enable and disable.
- [ ] Confirm that a new revision is required (env vars are baked into the revision).
- [ ] Confirm traffic migration behavior (auto or manual).
- [ ] Document whether written `life_events` should be cleaned up or kept after rollback.
- [ ] Document that pending actions in `pending` state survive rollback.

### 8. Test Data And Cleanup

**Test data strategy:**

- Use a dedicated test user account for smoke testing.
- Create test pending actions with identifiable patterns:
  - `actionId` containing `test_` prefix (e.g., `test_smoke_2026_06_29_001`).
  - Life event titles containing `[SMOKE TEST]` prefix.
- After smoke test, verify Firestore documents exist for the test user.
- After smoke test cleanup, verify Firestore documents are removed.

**Cleanup checklist:**

- [ ] Identify all test `life_event` documents created during smoke test.
- [ ] Identify all test `pending_action` documents created during smoke test.
- [ ] Delete test documents from Firestore (via console or Admin SDK).
- [ ] Verify no real user data was affected.
- [ ] Verify test user's `life_events` collection is empty after cleanup.
- [ ] Verify test user's `agent_pending_actions` collection is clean.

**Important**: Never clean up real user data. Cleanup is test-data-only.

## Suggested Phase 4.9 Steps

### Phase 4.9.1 - Document Production Enablement Checklist

**Goal**: Produce a single checklist document that enumerates every item that must be verified before enabling `create_life_event` real writes in production.

**Scope**:

- Consolidate all checklist items from this document into a concise, actionable format.
- Reference specific code files, env var names, and Cloud Run commands.
- No code changes.

**Out of Scope**:

- Implementing any checklist item.
- Modifying code, tests, rules, or env.

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (this document — may be refined).

**Acceptance Criteria**:

- [ ] Every production enablement prerequisite is listed.
- [ ] Each prerequisite has a clear pass/fail criterion.
- [ ] The checklist is actionable (someone other than the author could execute it).

**Required Checks**:

- `git diff --check`
- `git status --short`

### Phase 4.9.2 - Firestore Rules Proposal

**Goal**: Draft Firestore Rules for the `life_events` collection that enforce user-scoped access, and document the cross-project Auth constraint.

**Scope**:

- Draft rules for `users/{userId}/life_events/{eventId}`.
- Document the cross-project issue (Firestore project vs Auth project).
- Document whether frontend client SDK reads are needed or if backend-only reads suffice.
- Propose rules that deny client-side writes as defense-in-depth.
- Do **not** deploy rules.

**Out of Scope**:

- Deploying rules to production.
- Resolving the cross-project Auth issue (that is a Phase 5 concern).
- Modifying `agent_pending_actions` rules (backend-only collection, Admin SDK writes).

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (add rules draft section).
- Possibly `firestore.rules` (draft changes, not deployed).

**Acceptance Criteria**:

- [ ] Rules draft is written and reviewed.
- [ ] Cross-project Auth constraint is documented.
- [ ] Rules are validated with `firebase deploy --only firestore:rules --dry-run` (if file is modified).
- [ ] Rules are **not** deployed.

**Required Checks**:

- `firebase deploy --only firestore:rules --dry-run` (if rules file changed).
- `git diff --check`.

### Phase 4.9.3 - Cloud Run Env And Feature Flag Plan

**Goal**: Document the exact Cloud Run env var configuration for enabling and disabling `create_life_event` writes, including commands, verification steps, and rollback.

**Scope**:

- Document the two flag names and their values.
- Document `gcloud` commands for: set, verify, unset.
- Document that a new revision is required.
- Document rollback revision preparation.
- Document how to verify the flag state after deployment.

**Out of Scope**:

- Actually modifying Cloud Run env.
- Creating a staging environment.
- Setting up CI/CD for flag management.

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (add env plan section).

**Acceptance Criteria**:

- [ ] Exact `gcloud` commands are documented.
- [ ] Verification steps are documented.
- [ ] Rollback commands are documented.
- [ ] It is clear that no env change is made as part of this step.

**Required Checks**:

- `git diff --check`.

### Phase 4.9.4 - Backend Smoke Test Script / Manual Checklist

**Goal**: Create a manual smoke test checklist (and optionally a script) that validates the write path in a production-like environment.

**Scope**:

- Define each smoke test scenario (see section 4 above).
- Define expected results for each scenario.
- Optionally create a shell script or `curl`-based test sequence.
- Document how to run the smoke test locally with `USE_MOCK_AUTH=true` and `USE_MOCK_LLM=true`.
- Document how to run against production (with a test user) after flag enablement.

**Out of Scope**:

- Executing the smoke test against production.
- Automating smoke tests in CI/CD.
- Load testing or stress testing.

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (add smoke test section).
- Possibly `scripts/smoke-test-agent-confirm.sh` (new file, optional).

**Acceptance Criteria**:

- [ ] Smoke test checklist covers all 10 scenarios from section 4.
- [ ] Each scenario has pass/fail criteria.
- [ ] The checklist can be executed by someone other than the author.

**Required Checks**:

- `git diff --check`.
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj` (ensure existing tests still pass).

### Phase 4.9.5 - Frontend Readiness Review

**Goal**: Assess whether the frontend can safely coexist with backend-written `life_event` documents, without implementing any UI changes.

**Scope**:

- Check if `GET /api/life/events` returns agent-created events.
- Check if the frontend timeline component handles events with `source=agent`.
- Check if the frontend handles unexpected field shapes gracefully.
- Document findings and risk assessment.
- Decide whether frontend readiness is a blocker or a non-issue for initial smoke testing.

**Out of Scope**:

- Implementing frontend UI for agent-created life events.
- Adding source badges, edit/delete controls, or agent-specific display.
- Modifying any frontend code.

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (add frontend review section).

**Acceptance Criteria**:

- [ ] Frontend behavior with agent-created events is documented.
- [ ] It is clear whether frontend readiness blocks the smoke test.
- [ ] Phase 5 UI scope is documented.

**Required Checks**:

- `git diff --check`.

### Phase 4.9.6 - Rollback And Cleanup Plan

**Goal**: Document the complete rollback procedure and test data cleanup strategy.

**Scope**:

- Document the flag-based rollback procedure (Cloud Run env revert).
- Document what happens to already-written `life_events` after rollback.
- Document what happens to pending actions after rollback.
- Document the Firestore Rules rollback procedure.
- Define test data cleanup strategy (test user only).
- Define the cleanup checklist.

**Out of Scope**:

- Executing rollback.
- Cleaning up test data.
- Implementing automated cleanup scripts.

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (add rollback section).

**Acceptance Criteria**:

- [ ] Rollback procedure is documented with exact commands.
- [ ] Cleanup strategy is documented.
- [ ] It is clear what survives rollback and what does not.

**Required Checks**:

- `git diff --check`.

### Phase 4.9 Closeout

**Goal**: Confirm that all Phase 4.9 deliverables are complete and the project is ready for Phase 5 (production enablement execution).

**Scope**:

- Review all Phase 4.9 step acceptance criteria.
- Confirm that no code, env, rules, or frontend changes were made.
- Confirm that the production enablement checklist is complete and actionable.
- Confirm that Phase 5 boundary is clearly defined.

**Out of Scope**:

- Starting Phase 5 work.
- Enabling production writes.
- Deploying anything.

**Files Likely To Change**:

- `docs/phase4_9_production_enablement_preparation.md` (closeout section finalized).

**Acceptance Criteria**:

- [ ] All 4.9.x steps are complete.
- [ ] No code changes were made.
- [ ] Production enablement checklist is ready for Phase 5 execution.
- [ ] Phase 5 entry criteria are documented.

**Required Checks**:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
- `git diff --check`
- `git status --short`
- `git diff --stat`

## Acceptance Criteria

Phase 4.9 is complete when:

- [ ] The production enablement checklist is complete and actionable.
- [ ] The checklist clearly states: **Phase 4.9 does not enable production writes.**
- [ ] Firestore Rules draft is written, with cross-project Auth constraint documented.
- [ ] Cloud Run env var plan is documented with exact `gcloud` commands.
- [ ] Smoke test checklist is documented with all 10 scenarios.
- [ ] Rollback procedure is documented with exact commands.
- [ ] Frontend readiness is assessed and documented.
- [ ] Observability requirements are documented.
- [ ] Test data and cleanup strategy is documented.
- [ ] Phase 4.8 / 4.9 / 5 boundaries are clearly stated.
- [ ] No code, test, env, rules, or frontend changes were made.
- [ ] `git diff --check` shows no issues.
- [ ] `git status --short` shows only the new/modified docs.

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Feature flag accidentally enabled during unrelated Cloud Run update | Production writes start without smoke test | Document that flags must be explicitly checked before any Cloud Run deploy |
| Firestore Rules block Admin SDK writes (unlikely but possible) | Write path fails in production despite code being correct | Admin SDK bypasses rules by default; document this fact |
| Cross-project Auth issue prevents frontend `request.auth` validation in Firestore Rules | Rules cannot enforce user-scoped reads from client SDK | Document that backend-only reads are the safe default; defer client SDK reads to Phase 5 |
| Agent-created events have unexpected fields that break frontend timeline | UI displays raw or broken data | Phase 4.9.5 reviews this; if critical, add minimal filtering in Phase 5 |
| Test data left in production Firestore after smoke test | Data pollution | Cleanup checklist with explicit test markers |
| Rollback does not delete already-written life events | Orphan data | Accept this risk; life events are valid data, orphaned agent events can be cleaned manually |
| Developer forgets that service registration ≠ flag enabled | False sense of security | Document explicitly: "service registration does not mean production writing is enabled" |

## Out Of Scope

The following are explicitly **not** part of Phase 4.9:

- Enabling production real writes.
- Deploying to production Cloud Run.
- Modifying Firestore Rules in production.
- Building frontend UI for agent-created life events.
- Setting up a staging or canary environment.
- Adding new action types (`update_life_event`, `delete_life_event`, `create_reminder`, etc.).
- Implementing memory, reminder, calendar, or MCP integrations.
- Modifying the confirm endpoint or write coordinator.
- Adding new tests (Phase 4.8 test coverage is sufficient).
- CI/CD pipeline changes.
- Monitoring or alerting infrastructure.
- User-facing documentation or help content.

## Recommended Next Phase

**Phase 5.1 - Production Enablement Execution**

Phase 5 would be the first phase that actually makes production changes:

1. Apply Firestore Rules (if cross-project Auth is resolved).
2. Set Cloud Run env vars to enable writes for a test user (canary).
3. Execute the smoke test checklist from Phase 4.9.4.
4. Verify logs and observability signals.
5. If smoke test passes: enable for all users.
6. If smoke test fails: execute rollback per Phase 4.9.6.

Phase 5.2+ may cover:

- Frontend UI for agent-created life events (display, edit, delete).
- `update_life_event` and `delete_life_event` action types.
- Memory and reminder agent writes.
- Calendar and MCP integrations.
- User-facing agent activity log / audit trail.

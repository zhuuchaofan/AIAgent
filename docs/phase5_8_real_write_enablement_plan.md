# Phase 5.8 Real Write Enablement Plan

## Scope

Phase 5.8 is a planning-only phase for controlled `create_life_event` real-write enablement.

This phase does not deploy, does not modify Cloud Run env, does not enable write flags, does not modify Firestore Rules, and does not execute real writes.

## 1. Current Safety Baseline

Current deployed state:

- API revision: `life-agent-api-00035-tnf`.
- Web revision: `life-agent-web-00018-bpq`.
- Authenticated preview-only smoke passed in Phase 5.7.
- `create_life_event` planner routing works.
- Confirm flow remains preview-only while write flags are unset.
- No `life_event` was created during Phase 5.7.

Current Cloud Run env baseline:

```text
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set
ENABLE_CREATE_LIFE_EVENT_TOOL: not set
```

Current safety conclusion:

- Production real Agent writes are still disabled.
- `AgentWriteFeatureGate.CanCreateLifeEvent()` should return false.
- `/api/agent/confirm` should return `previewOnly=true` and `wroteData=false` for `create_life_event`.

## 2. Enablement Prerequisites

Before any real write enablement, all of the following must be true:

- Phase 5.7 authenticated preview-only smoke remains passing.
- Firestore Rules hardening decision is complete.
- Cross-project Auth behavior is understood:
  - Firestore project: `copper-affinity-467409-k7`.
  - Firebase Auth project: `my-agent-app-a5e42`.
- Rollback plan from Phase 5.4 is accepted.
- Smoke test user is dedicated and known.
- Test data prefix is agreed, for example `[SMOKE TEST]`.
- Observability logs are available in Cloud Run logs.
- Cleanup procedure for smoke `life_events` is accepted.
- User explicitly approves enabling real write flags.

Do not enable flags if any prerequisite is incomplete.

## 3. Cloud Run Env Flags

Real write enablement requires both flags:

```text
ENABLE_AGENT_WRITE_TOOLS=true
ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

Rules:

- `ENABLE_AGENT_WRITE_TOOLS=true` alone is not enough.
- `ENABLE_CREATE_LIFE_EVENT_TOOL=true` alone is not enough.
- If either flag is unset or false, confirm must remain preview-only.
- Service registration does not mean production writing is enabled.
- The feature gate remains the final control point for entering real-write path.

Potential enablement command, only after explicit approval:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --set-env-vars ENABLE_AGENT_WRITE_TOOLS=true,ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

This command creates a new Cloud Run revision. Phase 5.8 does not run it.

## 4. First Real-Write Canary Steps

Recommended canary flow:

1. Confirm current production env is preview-only.
2. Confirm current API revision and traffic.
3. Run authenticated preview-only smoke once more.
4. Enable both write flags only after explicit approval.
5. Confirm new Cloud Run revision receives traffic.
6. Run real-write smoke with a dedicated test user only.
7. Verify exactly one smoke `life_event` is created.
8. Repeat confirm for the same action and verify idempotency.
9. Disable write flags after canary unless continuing rollout is explicitly approved.
10. Record created `eventId`, `actionId`, user id, revision, and log correlation details.

Canary must not use real personal data.

## 5. Authenticated Real-Write Smoke Command

Real-write smoke must require all explicit env vars:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
RUN_AGENT_WRITE_SMOKE=true \
EXPECT_AGENT_WRITE_ENABLED=true \
SMOKE_TEST_PREFIX="[SMOKE TEST]" \
node scripts/smoke-agent-life-event-write.mjs
```

Expected real-write result:

- `/health` passes.
- Agent proposes `create_life_event`.
- Confirm returns `previewOnly=false`.
- Confirm returns `wroteData=true`.
- Confirm returns `createdResourceType=life_event`.
- Confirm returns `createdResourceId=evt_{agentActionId}`.
- Repeat confirm returns the same `createdResourceId`.
- Repeat confirm does not create a second event.

Never run this command against a real user account.

## 6. Firestore life_events Verification

Verify the created document under:

```text
users/{testUserId}/life_events/{createdResourceId}
```

Expected fields:

```text
id = createdResourceId
userId = testUserId
type = pet_health or expected smoke type
title contains [SMOKE TEST] or expected smoke title
content contains [SMOKE TEST] or expected smoke content
source = agent_confirmed
createdBy = agent
agentActionId = actionId
createdAt is present
updatedAt is present
structuredData contains only allowed fields
```

Do not inspect or export unrelated user documents.

## 7. Idempotency Verification

Idempotency checks:

- Confirm the same `actionId` twice.
- First confirm creates `createdResourceId=evt_{actionId}`.
- Second confirm returns the same `createdResourceId`.
- Second confirm does not call writer again.
- Firestore contains exactly one document for `evt_{actionId}`.
- Pending action stores:
  - `createdResourceType=life_event`
  - `createdResourceId=evt_{actionId}`
  - `wroteData=true`
  - `writeCompleted=true`
  - `writeCompletedAt` set

If duplicate documents appear, immediately rollback by disabling write flags.

## 8. Failure Rollback Steps

Immediate stop command, removing write flags:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --remove-env-vars ENABLE_AGENT_WRITE_TOOLS,ENABLE_CREATE_LIFE_EVENT_TOOL
```

Alternative explicit false command:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --set-env-vars ENABLE_AGENT_WRITE_TOOLS=false,ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

After rollback:

1. Confirm a new revision is serving traffic.
2. Confirm both write flags are unset or false.
3. Run preview-only smoke.
4. Check Cloud Run logs for `write_failed`, `wroteData=true`, `createdResourceId`, `invalid_payload`, and duplicate confirm events.
5. Identify affected `userId`, `actionId`, and `eventId`.
6. Clean only clearly marked smoke test data.
7. Do not proceed until root cause is documented.

If the new revision itself is broken, route traffic back to the previous known-good API revision:

```bash
gcloud run services update-traffic life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --to-revisions <previous-revision>=100
```

## 9. Prohibited Actions

Phase 5.8 prohibits:

- Deploying API or Web.
- Modifying Cloud Run env.
- Enabling `ENABLE_AGENT_WRITE_TOOLS`.
- Enabling `ENABLE_CREATE_LIFE_EVENT_TOOL`.
- Setting `RUN_AGENT_WRITE_SMOKE=true`.
- Setting `EXPECT_AGENT_WRITE_ENABLED=true`.
- Executing real `create_life_event` writes.
- Modifying Firestore Rules.
- Deploying Firestore Rules.
- Modifying Firebase Auth.
- Using real user data for canary.
- Creating reminder, memory, calendar, MCP, or additional action types.

## 10. Phase 5.8 Decision

Phase 5.8 does not allow real writes.

This document is only the real-write enablement plan. Any actual enablement requires a separate explicit approval and must start from the prerequisite checklist above.

Current recommendation:

- Keep production preview-only.
- Next phase may be a formal go/no-go checklist or an explicitly approved canary execution phase.

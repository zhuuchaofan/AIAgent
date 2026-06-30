# Phase 5.10 Controlled Real-Write Canary Execution Runbook

## Scope

Phase 5.10 is a Release Gate runbook and currently defaults to No-Go.

This document defines the exact checklist and commands for a future controlled `create_life_event` real-write canary. It does not authorize execution by itself.

Phase 5 Development is closed. This runbook is a release/operations artifact, not a development Phase.

Current phase restrictions:

- Do not deploy.
- Do not modify Cloud Run env.
- Do not enable `ENABLE_AGENT_WRITE_TOOLS`.
- Do not enable `ENABLE_CREATE_LIFE_EVENT_TOOL`.
- Do not set `RUN_AGENT_WRITE_SMOKE=true`.
- Do not set `EXPECT_AGENT_WRITE_ENABLED=true`.
- Do not execute real `create_life_event` writes.
- Do not modify Firestore Rules.
- Do not modify Firebase Auth.
- Do not use real personal user data.

Real-write canary execution requires another explicit user approval after all placeholders below are completed.

## 1. Final Confirmation Checklist

All items must be checked before execution.

| Item | Status | Notes |
| --- | --- | --- |
| User explicitly approves real-write canary | NO | Waiting for another explicit user approval. |
| Firestore Rules acceptance conclusion recorded | READY | Accepted for one backend Admin SDK canary; see section 2. |
| Dedicated smoke test userId recorded | READY | `2k5UiLxtfCaOZiUUc2yPbwpxQpw1`; see section 3. |
| Previous healthy API revision recorded | READY | Current verified preview-only revision: `life-agent-api-00035-tnf`. |
| Cloud Run logs query ready | READY | Copyable commands are listed in section 5. |
| Cleanup owner and scope confirmed | PARTIAL | Owner and test userId recorded; cleanup window and account data status remain TODO. |
| Current Cloud Run env confirmed preview-only | READY | Verified read-only: write flags unset, mock auth/LLM false. |
| Rollback commands ready | READY | See section 13. |
| No unrelated deployment in progress | TODO_USER_CONFIRM | Canary must be isolated. |

If any item is TODO, Phase 5.10 remains No-Go.

Current decision: No-Go. Do not execute canary until explicit user approval is recorded.

## 2. Firestore Rules Acceptance Conclusion

Record the final decision before canary:

```text
Decision: Accepted for one backend Admin SDK canary.
Accepted by: 小朱
Date/time: TODO_USER_CONFIRM
Rationale: Current create_life_event canary writes through the backend Admin SDK and does not depend on client direct Firestore access. Client read verification involving Firestore Rules will be validated separately.
```

Known considerations:

- Backend Admin SDK writes bypass Firestore Rules.
- The canary write path should not rely on client SDK writes.
- If frontend/client SDK reads are used to verify the created event, cross-project Auth behavior must be understood.
- Client direct create/update/delete posture for `life_events` must be accepted or hardened before broader rollout.

No-Go if the rules posture is unknown or disputed.

## 3. Dedicated Smoke Test User

Fill before canary:

```text
Test userId: 2k5UiLxtfCaOZiUUc2yPbwpxQpw1
Test account owner: 小朱
Account contains real personal data: TODO_USER_CONFIRM yes/no
Cleanup contact: 小朱
Smoke data marker: [SMOKE TEST]
```

Do not record the Firebase ID token, print the full token, or write the token to any repository file.

Requirements:

- Use only a dedicated test user.
- Do not run real-write smoke against a primary personal account.
- Smoke data must include `[SMOKE TEST]`.
- The userId must be known before Firestore verification and cleanup.

## 4. Previous Healthy API Revision

Fill before canary:

```text
Current preview-only API revision: life-agent-api-00035-tnf
Previous healthy rollback revision: life-agent-api-00035-tnf
Web revision: life-agent-web-00018-bpq
Read-only check result: life-agent-api-00035-tnf receives 100% traffic.
```

Expected current baseline from Phase 5.7:

```text
API revision: life-agent-api-00035-tnf
Web revision: life-agent-web-00018-bpq
```

Re-check immediately before canary because revisions may change.

## 5. Cloud Run Logs Query

Use Cloud Run logs to verify write-path behavior.

Suggested log queries:

```bash
gcloud logging read \
  'resource.type="cloud_run_revision"
   resource.labels.service_name="life-agent-api"
   jsonPayload.actionId="<ACTION_ID>"' \
  --project copper-affinity-467409-k7 \
  --limit 50 \
  --format json
```

Copyable query for the planned canary action:

```bash
gcloud logging read \
  'resource.type="cloud_run_revision"
   resource.labels.service_name="life-agent-api"
   (jsonPayload.actionId="<ACTION_ID>" OR textPayload:"<ACTION_ID>")' \
  --project copper-affinity-467409-k7 \
  --limit 100 \
  --format json
```

Copyable query for write results:

```bash
gcloud logging read \
  'resource.type="cloud_run_revision"
   resource.labels.service_name="life-agent-api"
   (jsonPayload.wroteData=true OR textPayload:"wroteData")' \
  --project copper-affinity-467409-k7 \
  --limit 100 \
  --format json
```

Copyable query for failure signals:

```bash
gcloud logging read \
  'resource.type="cloud_run_revision"
   resource.labels.service_name="life-agent-api"
   (textPayload:"write_failed" OR textPayload:"invalid_payload" OR textPayload:"cross-user")' \
  --project copper-affinity-467409-k7 \
  --limit 100 \
  --format json
```

If structured fields are emitted as text payloads in the current sink, search conservatively:

```bash
gcloud logging read \
  'resource.type="cloud_run_revision"
   resource.labels.service_name="life-agent-api"
   textPayload:"<ACTION_ID>"' \
  --project copper-affinity-467409-k7 \
  --limit 50 \
  --format json
```

Fields to verify:

- `actionId`
- `userId`
- `actionType`
- feature gate result
- `previewOnly`
- `wroteData`
- `createdResourceType`
- `createdResourceId`
- `write_failed`
- `invalid_payload`
- idempotent duplicate confirm

Do not log or copy Firebase tokens, auth headers, full payload JSON, secrets, or private content.

## 6. Cleanup Owner And Scope

Fill before canary:

```text
Cleanup owner: 小朱
Cleanup approver: 小朱
Cleanup window: TODO_USER_CONFIRM
Allowed cleanup path: users/{testUserId}/life_events
Allowed pending action path: users/{testUserId}/agent_pending_actions
Smoke data marker: [SMOKE TEST]
Current test userId: 2k5UiLxtfCaOZiUUc2yPbwpxQpw1
```

Cleanup is limited to:

- The dedicated test user only.
- `life_events` whose title/content include `[SMOKE TEST]`.
- `life_events` with `source=agent_confirmed`.
- `life_events` with `createdBy=agent`.
- `life_events` whose id matches `evt_{agentActionId}` from the canary.
- Corresponding smoke pending actions only if clearly identifiable.

Never bulk delete across users.

## 7. Pre-Canary Read-Only Env Check

Before enabling flags, run:

```bash
gcloud run services describe life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --format="yaml(status.url,status.traffic,spec.template.spec.containers[0].env)"
```

Required pre-canary state:

```text
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set or false
ENABLE_CREATE_LIFE_EVENT_TOOL: not set or false
```

Current read-only check:

```text
status.url = https://life-agent-api-hyo2yvwwia-uc.a.run.app
traffic = 100% to life-agent-api-00035-tnf
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set
ENABLE_CREATE_LIFE_EVENT_TOOL: not set
```

Stop if either write flag is already true.

## 8. Flags Enable Command

Run only after explicit user approval for real-write canary:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --set-env-vars ENABLE_AGENT_WRITE_TOOLS=true,ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

After enabling:

- Confirm a new Cloud Run revision is created.
- Confirm traffic is 100% to the intended revision.
- Confirm `USE_MOCK_AUTH=false`.
- Confirm `USE_MOCK_LLM=false`.

## 9. Real-Write Smoke Command

Run only after flags are enabled and verified:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
RUN_AGENT_WRITE_SMOKE=true \
EXPECT_AGENT_WRITE_ENABLED=true \
SMOKE_TEST_PREFIX="[SMOKE TEST]" \
node scripts/smoke-agent-life-event-write.mjs
```

Expected result:

- `/health` passes.
- Agent proposes `create_life_event`.
- Confirm returns `previewOnly=false`.
- Confirm returns `wroteData=true`.
- Confirm returns `createdResourceType=life_event`.
- Confirm returns `createdResourceId=evt_{agentActionId}`.
- Repeat confirm returns the same `createdResourceId`.
- Repeat confirm does not create another event.

Stop if the smoke runs against the wrong user or if expected fields are missing.

## 10. Firestore Verification Steps

Verify only the dedicated test user path:

```text
users/{testUserId}/life_events/{createdResourceId}
```

Expected document:

```text
id = createdResourceId
userId = testUserId
source = agent_confirmed
createdBy = agent
agentActionId = actionId
createdAt is present
updatedAt is present
structuredData contains only allowed fields
title/content include [SMOKE TEST] or agreed smoke marker
```

Also verify pending action:

```text
users/{testUserId}/agent_pending_actions/{actionId}
```

Expected pending action fields:

```text
status = confirmed
createdResourceType = life_event
createdResourceId = evt_{actionId}
wroteData = true
writeCompleted = true
writeCompletedAt is present
```

Do not inspect unrelated user data.

## 11. Idempotency Verification

The smoke script repeats confirm for the same `actionId`.

Expected idempotency:

- Same `createdResourceId`.
- No second `life_event` document.
- Logs show idempotent return or duplicate confirm handling.
- Pending action remains confirmed and write-completed.

Manual Firestore check:

```text
Count documents where id == evt_{actionId}: exactly 1
```

Stop if duplicate events are created.

## 12. Stop Conditions

Immediately stop and rollback if any condition occurs:

- Wrong test user.
- `USE_MOCK_AUTH=true`.
- `USE_MOCK_LLM=true`.
- `previewOnly=true` after flags are confirmed true.
- `wroteData=false` in real-write smoke.
- Missing `createdResourceId`.
- Created resource id does not equal `evt_{actionId}`.
- More than one `life_event` is created for the action.
- Firestore write fails with `write_failed`.
- `invalid_payload` appears.
- Logs contain Firebase token, auth header, secret, or full private payload.
- RAG or Agent Preview main flow regresses.
- Cleanup path cannot be determined.

## 13. Rollback Commands

Preferred rollback, remove flags:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --remove-env-vars ENABLE_AGENT_WRITE_TOOLS,ENABLE_CREATE_LIFE_EVENT_TOOL
```

Alternative rollback, set flags false:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --set-env-vars ENABLE_AGENT_WRITE_TOOLS=false,ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

If revision rollback is required:

```bash
gcloud run services update-traffic life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --to-revisions <previous-healthy-revision>=100
```

After rollback:

- Confirm traffic points to the rollback revision or new flags-off revision.
- Confirm write flags are unset or false.
- Run preview-only smoke without real-write env vars.

## 14. Cleanup Checklist

Run cleanup only after recording canary evidence.

Checklist:

1. Confirm `testUserId`.
2. Confirm `actionId`.
3. Confirm `createdResourceId`.
4. Open `users/{testUserId}/life_events/{createdResourceId}`.
5. Confirm `[SMOKE TEST]`, `source=agent_confirmed`, `createdBy=agent`, and `agentActionId`.
6. Delete only that smoke `life_event` if cleanup is approved.
7. Inspect `users/{testUserId}/agent_pending_actions/{actionId}`.
8. Clean the corresponding smoke pending action only if approved.
9. Re-check no additional smoke event remains for the action.
10. Do not delete any non-smoke data.

## 15. Authorization Boundary

This runbook does not authorize execution.

Only a later explicit user approval may authorize Phase 5.10 canary execution. That approval must specifically allow:

- Enabling `ENABLE_AGENT_WRITE_TOOLS=true`.
- Enabling `ENABLE_CREATE_LIFE_EVENT_TOOL=true`.
- Setting `RUN_AGENT_WRITE_SMOKE=true`.
- Setting `EXPECT_AGENT_WRITE_ENABLED=true`.
- Running one controlled real `create_life_event` write using the dedicated smoke user.

Until that approval is given, this Release Gate runbook remains No-Go.

## 16. Current No-Go Summary

Current state remains No-Go.

Blocking manual confirmations:

- `User explicit approval for real-write canary = NO`
- `Account contains real personal data = TODO_USER_CONFIRM`
- `Cleanup window = TODO_USER_CONFIRM`

Therefore:

- Do not deploy.
- Do not modify Cloud Run env.
- Do not enable write flags.
- Do not set real-write smoke env vars.
- Do not execute real writes.

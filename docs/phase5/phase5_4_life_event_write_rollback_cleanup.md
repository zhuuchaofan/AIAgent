# Phase 5.4 Life Event Write Rollback And Cleanup Plan

## Scope

Phase 5.4 defines rollback and cleanup procedures for future controlled enablement of Agent `create_life_event` writes.

This phase does not enable real writes, does not change Cloud Run env, does not deploy, does not modify Firestore Rules, does not delete Firestore data, and does not add automatic cleanup scripts.

## Feature Flag Rollback

Real Agent writes are controlled by two flags:

```text
ENABLE_AGENT_WRITE_TOOLS
ENABLE_CREATE_LIFE_EVENT_TOOL
```

Either flag being unset or false must keep `/api/agent/confirm` in preview-only mode for `create_life_event`.

Recommended rollback command, removing the flags:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --remove-env-vars ENABLE_AGENT_WRITE_TOOLS,ENABLE_CREATE_LIFE_EVENT_TOOL
```

Alternative rollback command, explicitly setting both flags to false:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --set-env-vars ENABLE_AGENT_WRITE_TOOLS=false,ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

Notes:

- Updating Cloud Run env creates a new revision.
- After the rollback revision receives traffic, `AgentWriteFeatureGate.CanCreateLifeEvent()` should return false.
- `/api/agent/confirm` should return to preview-only behavior.
- New confirms should not create `life_event` documents.

## Rollback Verification

Health check:

```bash
curl https://life-agent-api-151587524132.us-central1.run.app/health
```

Expected response:

```text
healthy
```

Preview-only smoke after rollback:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="..." \
node scripts/smoke-agent-life-event-write.mjs
```

Expected confirm result for a new action:

```json
{
  "previewOnly": true,
  "wroteData": false
}
```

Expected data behavior:

- No new `life_event` is created.
- No `users/{userId}/life_events/{eventId}` write occurs.
- Pending action can still complete its preview confirmation lifecycle.

This verification does not require real-write smoke flags and must not require executing a real write.

## Already Written Life Events

Rollback does not automatically delete already-created `life_event` documents.

Policy:

- Real user life records created before rollback are retained by default.
- Smoke test data may be cleaned manually.
- Do not bulk delete non-smoke data.
- Cleanup must be limited to a known test user and documents clearly marked with `[SMOKE TEST]`.
- Any cleanup of real user records requires a separate explicit product and data-retention decision.

## Smoke Test Data Markers

Smoke-created data must be identifiable.

Required markers:

- `title` contains `[SMOKE TEST]`.
- `content` contains `[SMOKE TEST]`.
- `source=agent_confirmed`.
- `createdBy=agent`.
- `agentActionId` exists.
- `createdResourceId=evt_{agentActionId}` in the corresponding pending action response/state.

Recommended path pattern:

```text
users/{testUserId}/life_events/evt_{agentActionId}
```

## Manual Test Data Cleanup Checklist

Use this checklist only for a dedicated test user.

1. Confirm the exact `testUserId`.
2. Open `users/{testUserId}/life_events`.
3. Filter or inspect only documents whose `title` or `content` contains `[SMOKE TEST]`.
4. Confirm each candidate has `source=agent_confirmed` and `createdBy=agent`.
5. Record every `eventId` selected for deletion.
6. Delete only the confirmed smoke `life_event` documents.
7. Open `users/{testUserId}/agent_pending_actions`.
8. Locate pending actions whose `createdResourceId` matches deleted smoke `eventId` values or whose payload/title/content contains `[SMOKE TEST]`.
9. Clean corresponding smoke pending actions only if they are clearly test artifacts.
10. Re-check both collections and confirm no real user data was deleted.

Do not write an automatic delete script until the test data markers and retention policy are validated in production-like data.

## Pending Action Rollback Behavior

Expected behavior after rollback:

- Actions already `confirmed` with `wroteData=true` remain `confirmed`.
- Repeating confirm on already written actions must not create another `life_event`.
- Pending actions that have not written data should take the preview-only path when flags are false.
- `write_failed` actions may be retried later if flags are intentionally enabled again.
- Do not automatically bulk update pending action statuses during rollback.

Rationale:

- Pending actions are audit records.
- Bulk mutation risks losing the source trail for created or attempted writes.
- Feature flag rollback is the primary stop mechanism.

## Firestore Rules Rollback

Future Phase 5 work may deploy hardened Firestore Rules for `life_events`.

If rules deployment causes an issue:

- Roll back to the previous known-good Firestore Rules release.
- Verify owner read behavior for `users/{userId}/life_events/{eventId}`.
- Verify client direct create/update/delete remains aligned with the intended security posture.

Notes:

- Phase 5.4 does not deploy Firestore Rules.
- Firestore Rules rollback does not affect backend Admin SDK writes.
- Cross-project Auth behavior between Firebase Auth project `my-agent-app-a5e42` and Firestore project `copper-affinity-467409-k7` still requires explicit verification before broad client read/write rule changes.

## Incident Checklist

If production write enablement causes abnormal behavior:

1. Immediately disable `ENABLE_AGENT_WRITE_TOOLS`.
2. Confirm a new Cloud Run revision is created and receives traffic.
3. Run `/health`.
4. Run preview-only smoke without `RUN_AGENT_WRITE_SMOKE`.
5. Query Cloud Run logs for:
   - `write_failed`
   - `wroteData=true`
   - `createdResourceId`
   - `invalid_payload`
   - `duplicate confirm`
   - `cross-user reject`
6. Identify affected `userId`, `actionId`, and `eventId`.
7. Clean only clearly marked smoke data.
8. Preserve real user data unless a separate approved data correction plan exists.
9. Record incident timeline, affected scope, root cause, and rollback revision.
10. Do not continue to Phase 5.5 until the cause is understood and documented.

## What Phase 5.4 Does Not Do

Phase 5.4 does not:

- Enable real Agent writes.
- Change Cloud Run env.
- Deploy API or Web.
- Modify Firestore Rules.
- Deploy Firestore Rules.
- Delete any Firestore data.
- Add automatic deletion scripts.
- Modify backend business code.
- Modify frontend code.
- Execute real `create_life_event` writes.
- Implement reminder, memory, calendar, MCP, or additional action types.

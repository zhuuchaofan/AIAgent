# Phase 4.7 Persistent Confirmation Acceptance

## Goal

Phase 4.7 adds persistent confirmation infrastructure before real Agent writes are enabled.

The system still does not create reminders, life events, memories, calendar events, MCP actions, or multi-agent work.

## Current Capability

- Agent Preview can create a pending `proposedAction` for write-like user intent.
- Pending actions are persisted in Firestore under `users/{userId}/agent_pending_actions/{actionId}`.
- The lifecycle state machine remains:
  - `created`
  - `pending`
  - `confirmed`
  - `cancelled`
  - `expired`
- Confirm and cancel are preview-only.
- Confirmation responses include `previewOnly=true` and `wroteData=false`.
- Repeating the same terminal decision is idempotent:
  - confirmed + confirm returns confirmed without another execution semantic.
  - cancelled + cancel returns cancelled without another execution semantic.
- Conflicting terminal decisions are rejected:
  - cancelled + confirm fails.
  - confirmed + cancel fails.
- Users can only access their own pending actions.
- Expired pending actions cannot be confirmed.

## Firestore Document Shape

Pending actions are stored below the authenticated user:

```text
users/{userId}/agent_pending_actions/{actionId}
```

Required audit fields:

- `userId`
- `actionType`
- `status`
- `lifecycleStatus`
- `previewOnly`
- `createdAt`
- `updatedAt`
- `confirmedAt`
- `cancelledAt`
- `expiredAt`
- `expiresAt`

Payloads are stored as `payloadJson` for preview/audit only. They must not be executed as real business writes in Phase 4.7.

## Manual Production Acceptance

1. Open `https://life.zhuchaofan.com/`.
2. Log in with a real Firebase user.
3. Enter the `知识库问答 (RAG)` tab.
4. Expand `Agent Preview`.
5. Send `明天提醒我观察黑猫`.
6. Verify a confirmation card appears.
7. Click confirm.
8. Verify the UI shows confirmed / preview success / no data written.
9. Send `明天提醒我观察黑猫` again.
10. Click cancel.
11. Verify the UI shows cancelled / no data written.
12. Verify no real reminder, life event, or memory was created.
13. Optionally inspect Firestore and verify only pending confirmation audit documents were written under `agent_pending_actions`.

## Automated Verification

Backend tests:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

Frontend checks:

```bash
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
```

Smoke tests:

```bash
node scripts/smoke-rag-e2e.mjs
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" node scripts/smoke-rag-e2e.mjs
```

Authenticated Agent Preview smoke requires:

```bash
FIREBASE_ID_TOKEN="..." API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" node scripts/smoke-rag-e2e.mjs
```

If `FIREBASE_ID_TOKEN` is missing, authenticated Agent/RAG checks must SKIP instead of failing.

Patch hygiene:

```bash
git diff --check
```

## Non-Goals

Phase 4.7 must not:

- Implement real `create_reminder`.
- Implement real `save_memory`.
- Implement real `create_life_event`.
- Connect Google Calendar.
- Connect MCP.
- Add multi-agent orchestration.
- Change Firebase Auth.
- Change Firestore Rules unless separately reviewed.
- Change the main RAG path.
- Let `/api/agent/confirm` write real business data.

## Phase 5 Precondition

Before real writes are enabled, Phase 5 must add an explicit executor boundary for confirmed actions, with authorization, idempotency keys, audit review, and tests for each real write target.

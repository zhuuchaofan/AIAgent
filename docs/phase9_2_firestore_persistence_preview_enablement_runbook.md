# Phase 9.2 Firestore Persistence Preview Enablement Runbook

Date: 2026-07-10

## Executive Summary

This runbook prepares the approved enablement path for Personal Agent v2
Firestore-backed pending action persistence.

Current status: **prepared, not executed**.

This document does not modify Cloud Run env, does not deploy, does not connect
to Firestore, and does not write real data. It is the checklist to use only
after explicit approval for:

1. Cloud Run env change,
2. preview deployment,
3. authenticated persistence smoke,
4. preview-only Firestore writes under
   `users/{userId}/pendingActions/{pendingActionId}`.

## Scope

Allowed future scope after approval:

- Personal Agent v2 pending action state memory only
- path: `users/{userId}/pendingActions/{pendingActionId}`
- create pending action
- refresh and restore pending action
- confirm pending action
- cancel pending action
- list history
- verify owner isolation

Out of scope:

- `life_events`
- `memories`
- real tool execution
- external provider execution
- Firestore Rules deployment
- IAM mutation unless separately approved
- frontend direct Firestore access

## Preconditions

Before any enablement action, verify:

1. `main` working tree is clean.
2. Local validation passes:
   - `git diff --check`
   - `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
   - `npm --prefix life-agent-web run lint`
3. API Cloud Run env is readable.
4. API dangerous write flags are unset or false:
   - `ENABLE_AGENT_WRITE_TOOLS`
   - `ENABLE_CREATE_LIFE_EVENT_TOOL`
5. API mock flags are unset or false:
   - `USE_MOCK_AUTH`
   - `USE_MOCK_LLM`
6. Web Agent Preview remains enabled:
   - `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW=true`
7. User explicitly accepts or separately mitigates the broad service-account
   IAM risk documented in
   `docs/phase9_1_firestore_persistence_release_gate_review.md`.
8. A real authenticated smoke path is available.

If any precondition fails, stop before env mutation or deployment.

## Env Change Plan

Change only these API env vars:

```text
AGENT_PENDING_ACTION_STORE_MODE=firestore
AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true
AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true
```

Do not set, update, or enable:

```text
ENABLE_AGENT_WRITE_TOOLS
ENABLE_CREATE_LIFE_EVENT_TOOL
```

Do not change:

- Firebase project config
- `firestore.rules`
- `firebase.json`
- web Firebase config
- secrets
- memory or life-event env flags

Expected API metadata after enablement:

```json
{
  "storeMode": "firestore",
  "firestorePersistenceEnabled": true,
  "previewOnly": true,
  "safetyMode": "personal_agent_v2_firestore_persistence_preview_only"
}
```

## Deployment Plan

Deployment requires explicit approval before execution.

Deploy order:

1. API service: `life-agent-api`
2. Web service: `life-agent-web` only if frontend code or build env needs a
   new revision

Project and region:

```text
project: copper-affinity-467409-k7
region: us-central1
```

Before deployment, record:

- API latest ready revision
- API traffic split
- Web latest ready revision
- Web traffic split
- relevant env flag states without secret values

After deployment, record:

- new API revision
- new Web revision if deployed
- traffic split
- env flag states without secret values

## Authenticated Smoke Checklist

Run this only after explicit approval and deployment.

### Service Readiness

1. API `/health` returns 200.
2. `https://life.zhuchaofan.com/` returns 200.
3. User can log in with Firebase Auth.
4. Agent Preview is visible.

Scripted smoke command after approval:

```bash
API_BASE_URL="https://<life-agent-api-url>" \
FIREBASE_ID_TOKEN="<user-a-token>" \
FIREBASE_ID_TOKEN_B="<optional-user-b-token>" \
RUN_PERSONAL_AGENT_V2_PERSISTENCE_SMOKE=true \
EXPECT_PERSONAL_AGENT_FIRESTORE_PERSISTENCE=true \
node scripts/smoke-personal-agent-v2-persistence.mjs
```

The script checks `/health` first. Without
`RUN_PERSONAL_AGENT_V2_PERSISTENCE_SMOKE=true`, it skips pending action
mutation checks. Without `FIREBASE_ID_TOKEN`, it skips authenticated checks.

### Persistence Metadata

1. Load Personal Agent v2 pending action list.
2. Confirm API/UI metadata:
   - `storeMode=firestore`
   - `firestorePersistenceEnabled=true`
   - `previewOnly=true`
   - `safetyMode=personal_agent_v2_firestore_persistence_preview_only`

### Create / Refresh Restore

1. Create a pending action.
2. Confirm action displays:
   - `status=pending`
   - `executed=false`
   - `wroteData=false`
   - `legacyConfirm=false`
   - `realWritePath=false`
3. Refresh the browser.
4. Confirm the same action remains visible.

### Confirm / Refresh Restore

1. Confirm the pending action.
2. Confirm action displays:
   - `status=confirmed`
   - `confirmed != executed`
   - `executed=false`
   - `wroteData=false`
   - `legacyConfirm=false`
   - `realWritePath=false`
3. Refresh the browser.
4. Confirm the confirmed action remains visible and has no active confirm/cancel
   controls.

### Cancel / Refresh Restore

1. Create a second pending action.
2. Cancel it.
3. Confirm action displays:
   - `status=cancelled`
   - `executed=false`
   - `wroteData=false`
   - no active confirm/cancel controls
4. Refresh the browser.
5. Confirm the cancelled action remains visible.

### Owner Isolation

Preferred if a second test user is available:

1. User A creates a pending action.
2. User B tries to confirm or fetch the same action through the v2 API.
3. Expected result:
   - HTTP 404
   - `status=not_found`
   - no owner data leak

If no second test user is available, record owner-isolation smoke as pending
and rely only on local tests until a second authenticated user is available.

### Negative Safety Checks

Confirm all remain true:

- no `life_events` write was triggered
- no `memories` write was triggered
- no real tool execution occurred
- old `/api/agent/confirm` was not used for Personal Agent v2 actions
- dangerous write flags stayed unset or false

## Stop Conditions

Stop immediately and rollback or report if any of the following occurs:

- Cloud Run env cannot be read before mutation.
- `ENABLE_AGENT_WRITE_TOOLS=true`.
- `ENABLE_CREATE_LIFE_EVENT_TOOL=true`.
- both dangerous write flags are true.
- `USE_MOCK_AUTH=true`.
- `USE_MOCK_LLM=true`.
- API metadata does not report `firestorePersistenceEnabled=true` after
  enablement.
- confirmed action becomes `executed=true`.
- any action reports `wroteData=true`.
- any action reports `realWritePath=true`.
- old `/api/agent/confirm` is used by the v2 UI path.
- `life_events` or `memories` writes are observed.
- refresh restore fails after a successful create/confirm/cancel.
- cross-user access returns anything other than not found.

## Rollback Plan

Preferred rollback:

1. Set `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=false`.
2. Keep dangerous write flags unset or false.
3. Deploy/shift traffic according to the Cloud Run deployment skill.
4. Verify API metadata returns:
   - `firestorePersistenceEnabled=false`
   - `safetyMode=personal_agent_v2_in_memory_preview_only`

Alternative rollback:

- set `AGENT_PENDING_ACTION_STORE_MODE=in_memory`
- or set `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=false`
- or shift Cloud Run traffic back to the previous known-good revision

No business-data rollback is expected because this release gate must not write
`life_events`, `memories`, or execute real tools.

Firestore preview pending action documents may remain as preview-only state
records. Do not manually delete them unless a separate cleanup plan is approved.

## Result Document Template

After execution, add a result document that records:

- execution time
- approver
- project / region
- API service
- Web service
- pre-deployment revisions
- post-deployment revisions
- env flags changed
- dangerous write flag states
- persistence metadata
- smoke result
- authenticated user scope
- owner-isolation result
- whether rollback occurred
- final conclusion:
  - deployed successfully
  - deployed with smoke pending
  - rolled back
  - stopped before deployment

## Current Conclusion

Personal Agent v2 has no known local implementation blocker for Firestore
pending action persistence preview enablement. The remaining blocker is
explicit approval for Cloud Run env mutation, preview deployment, preview-only
Firestore writes, and authenticated smoke.

# Phase 5.1 Firestore Rules Hardening Draft

## Scope

Phase 5.1 drafts Firestore Rules hardening for production `create_life_event` enablement.

This phase does not deploy rules, does not enable Agent writes, does not modify Cloud Run env, does not deploy API/Web, does not change Firebase Auth, and does not execute a real `create_life_event` write.

This document is a draft. The repository `firestore.rules` file was intentionally not changed in this phase to avoid implying that hardened rules are ready to deploy.

## Current Rules File

Rules file discovered:

```text
./firestore.rules
```

Current `life_events` rule:

```rules
match /users/{userId}/life_events/{eventId} {
  allow read: if isOwner(userId);
  allow create: if isOwner(userId)
    && hasRequiredString('type')
    && hasRequiredString('content')
    && hasRequiredString('title')
    && request.resource.data['userId'] == userId
    && request.resource.data['isDeleted'] == false
    && isNumberInRange('importance', 1, 5);
  allow update: if isOwner(userId)
    // userId and createdAt are immutable after creation
    && request.resource.data['userId'] == resource.data['userId']
    && request.resource.data['createdAt'] == resource.data['createdAt'];
  allow delete: if isOwner(userId);
}
```

Current behavior:

- Authenticated users can read their own `life_events`.
- Authenticated users can directly create their own `life_events` via client SDK if schema checks pass.
- Authenticated users can directly update their own `life_events` via client SDK if immutable field checks pass.
- Authenticated users can directly delete their own `life_events` via client SDK.

This is incompatible with the Phase 4.8 Agent write security boundary, where `life_events` writes should flow through backend-controlled APIs and validators.

## Hardened Rule Goal

Target client SDK behavior:

- Allow users to read their own `life_events`.
- Deny all client direct create/update/delete for `life_events`.
- Keep backend Admin SDK writes available. Admin SDK bypasses Firestore Rules.

Suggested target rule:

```rules
match /users/{userId}/life_events/{eventId} {
  allow read: if request.auth != null && request.auth.uid == userId;
  allow create, update, delete: if false;
}
```

Equivalent form using existing helpers:

```rules
match /users/{userId}/life_events/{eventId} {
  allow read: if isOwner(userId);
  allow create, update, delete: if false;
}
```

The explicit `request.auth.uid == userId` form is easier to audit in isolation. The `isOwner(userId)` helper is acceptable if it remains:

```rules
function isOwner(userId) {
  return isAuthenticated() && request.auth.uid == userId;
}
```

## Backend Write Path Impact

The Agent write path uses backend services:

```text
/api/agent/confirm
  -> AgentWriteFeatureGate
  -> LifeEventActionPayloadMapper
  -> LifeEventPayloadValidator
  -> AgentLifeEventConfirmationWriteCoordinator
  -> FirestoreAgentLifeEventWriter
  -> users/{userId}/life_events/{eventId}
```

The backend uses Google Cloud credentials / Admin SDK style server access. Firestore Rules protect client SDK access and do not block trusted backend writes.

Expected impact after hardened rules:

- Agent confirmed writes can still work through backend service credentials.
- Manual `POST /api/life/ingest` can still write through backend service credentials.
- Existing backend `PUT /api/life/events/{id}` and `DELETE /api/life/events/{id}` can still update/delete through backend service credentials.
- Direct client SDK writes to `life_events` are blocked.

## Frontend Impact

Current frontend event access uses backend actions:

```text
life-agent-web/src/app/actions/events.ts
  -> GET /api/life/events
  -> POST /api/life/ingest
  -> PUT /api/life/events/{id}
  -> DELETE /api/life/events/{id}
```

The current frontend does not appear to use the Firestore client SDK directly for `life_events`.

Expected impact:

- Read/list via backend API should continue working.
- Ingest/edit/delete via backend API should continue working.
- If any hidden or future frontend path uses Firestore client SDK directly for `life_events` writes, it will fail after hardening. That is intended.

## Cross-Project Auth Risk

Current project split:

```text
Firestore project: copper-affinity-467409-k7
Firebase Auth project: my-agent-app-a5e42
```

Risk:

1. Firestore Rules use `request.auth`.
2. Whether `request.auth.uid == userId` works depends on Firestore project / Firebase Auth configuration.
3. If Auth and Firestore are not configured to work together, owner read checks may fail even when the API token is valid for the backend.
4. Backend Admin SDK writes are not blocked by this risk because they bypass Firestore Rules.
5. If the frontend continues using backend APIs for reads, this is not a blocker for Agent `create_life_event` backend writes.
6. If future frontend code directly reads `life_events` via Firestore client SDK, the Auth/Firestore project relationship must be verified first.

Required verification before deploying hardened rules:

- Confirm whether Firestore Rules in `copper-affinity-467409-k7` can authenticate tokens from `my-agent-app-a5e42`.
- Test owner read with a real frontend-authenticated user.
- Test cross-user read denial.
- Test direct client create/update/delete denial.
- Test backend API list/ingest/update/delete still works.

## Draft Test Matrix

Before deploying hardened rules, run a rules test or emulator test matrix:

| Scenario | Expected |
|---|---|
| Unauthenticated read `users/{userId}/life_events/{eventId}` | Deny |
| Owner read own event | Allow, if cross-project Auth is valid |
| Other user read event | Deny |
| Owner client create event | Deny |
| Owner client update event | Deny |
| Owner client delete event | Deny |
| Backend API `GET /api/life/events` | Pass |
| Backend API `POST /api/life/ingest` | Pass |
| Backend API `PUT /api/life/events/{id}` | Pass |
| Backend API `DELETE /api/life/events/{id}` | Pass |
| Agent confirm with write flags false | Preview-only, no write |
| Agent confirm with write flags true in controlled env | Backend write pass, client rules not involved |

## Deployment Plan Draft

Do not deploy in Phase 5.1.

Future deployment sequence:

1. Freeze hardened rules.
2. Run emulator/rules tests.
3. Verify cross-project Auth behavior.
4. Run backend API local tests.
5. Deploy rules only after approval.
6. Run non-mutating smoke.
7. Run backend API life event smoke.
8. Only later consider enabling Agent write flags.

## Rollback Draft

If hardened rules break client reads or backend-mediated flows:

1. Revert Firestore Rules to the previous known-good ruleset.
2. Deploy the rollback ruleset.
3. Keep Agent write flags false.
4. Run:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" node scripts/smoke-rag-e2e.mjs
```

5. Manually verify Timeline event list for a known test user.

## Phase 5.1 Recommendation

Do not enable production real Agent writes after this phase.

Recommended next step:

- Add Firestore Rules emulator/static tests or a documented dry-run procedure.
- Decide whether to harden `firestore.rules` in a separate commit.
- Verify cross-project Auth before any rules deployment.

Conclusion:

```text
RULES HARDENING DRAFT READY.
RULES NOT DEPLOYED.
PRODUCTION REAL WRITES STILL DISABLED.
PHASE 5.2 SHOULD HANDLE RULES TESTING / DEPLOYMENT PREP, NOT WRITE ENABLEMENT.
```

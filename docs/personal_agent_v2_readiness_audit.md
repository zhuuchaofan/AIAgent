# Personal Agent v2 Readiness Audit

Date: 2026-07-11

## Executive Summary

Personal Agent v2 is deployed with Firestore-backed pending action state memory
enabled for preview-only Agent Preview actions.

Current decision: **GO for Personal Agent v2 closeout on the main user-visible
path; deployed cross-user smoke remains a recommended follow-up when a second
test account is available.**

## Current Runtime Map

Production runtime after Firestore persistence enablement:

```text
AgentPreview.tsx
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> PendingActionStoreFactory
  -> FirestorePendingActionStore
  -> users/{userId}/pendingActions/{pendingActionId}
```

Safe rollback route:

```text
AgentPreview.tsx
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> PendingActionStoreFactory
  -> InMemoryPendingActionStore
```

Legacy route:

```text
/api/agent/confirm
  -> IPendingAgentActionStore
  -> legacy PendingAgentAction
```

The legacy route remains separate and is not the Personal Agent v2 persistence
path.

## Requirement Matrix

| Requirement | Current evidence | Status |
| --- | --- | --- |
| Create pending action | `Phase80PendingActionRuntime.CreateAsync`; `/api/agent/pending-actions`; tests in `Phase80PendingActionRuntimeMvpTest` and `Phase9PendingActionPersistenceTest`; user browser smoke created persisted actions | Complete |
| Refresh restores state | User-provided browser evidence shows confirmed and cancelled actions remained visible after refresh with Firestore preview mode | Complete on main user path |
| View historical pending actions | `ListAsync` returns all user-scoped records, including confirmed and cancelled; `ListReturnsHistoricalConfirmedAndCancelledActions` covers this; user smoke showed history after refresh | Complete |
| Confirm persists status | `ConfirmStatusPersistsAndDoesNotExecute` covers status persistence; user smoke showed confirmed action persisted after refresh | Complete |
| Cancel persists status | `CancelStatusPersistsAndCannotConfirm` covers status persistence; user smoke showed cancelled action persisted after refresh | Complete |
| User can only access own data | `CrossUserAccessIsBlocked`; endpoint owner comes from auth context; cross-user endpoint confirmation returns 404; endpoint same-id owner scope test covers collision safety | Locally proven; deployed second-account smoke pending |
| Status transitions are safe | `PendingActionTransitionPolicy`; `TransitionPolicyRejectsUnsafeStatusChanges`; direct `executed` transition rejected; finalized records reject late metadata, guard, and status mutations | Complete |
| Duplicate create does not overwrite payload | Store create rejects duplicate `pendingActionId` writes with a different idempotency key; original payload snapshot remains authoritative | Complete |
| Audit metadata is required | Shared create validator rejects missing idempotency, trace, preview, payload, policy, and audit refs before store write | Complete |
| Agent does not auto execute | `executed=false`, `wroteData=false`, `executionReady=false`; store serializers force false; user smoke showed false safety flags after refresh | Complete |
| Firestore persistence enabled | API Cloud Run env selects Firestore preview-only persistence; `life-agent-api-00042-zhr` serves 100% traffic | Complete |
| Production smoke proves refresh restore | User browser smoke verified refresh restore and persisted confirmed/cancelled history | Complete on main user path |
| Release smoke tooling | `scripts/smoke-personal-agent-v2-persistence.mjs` health smoke passed; scripted authenticated flow remains pending without Firebase ID token | Partially complete |

## Local Implementation Evidence

Backend:

- `IPendingActionStore` is the mainline store contract.
- `InMemoryPendingActionStore` remains the safe rollback store.
- `FirestorePendingActionStore` is the deployed preview-only durable store.
- `PendingActionStoreFactory` owns mode selection.
- `PendingActionPersistenceOptions` requires explicit Firestore approval and
  `PreviewOnly=true`; it parses Cloud Run env-style keys and invalid values
  fail safe to in-memory.
- `PendingActionTransitionPolicy` centralizes status transition checks.
- `InMemoryPendingActionStore` uses owner-scoped keys
  `(userSubjectRef, pendingActionId)`, matching the intended Firestore access
  boundary and preventing cross-user collisions for the same action id.
- Store create rejects duplicate `pendingActionId` writes with a different
  idempotency key, preserving the original payload snapshot instead of
  overwriting it.
- `AgentEndpoints` exposes `/api/agent/pending-actions` as the v2 path.
- v2 endpoint failures use release-smoke-friendly HTTP status codes:
  - missing or cross-user action: 404
  - finalized state conflict: 409
  - error bodies preserve safe action view metadata when available

Frontend:

- `AgentPreview.tsx` uses the v2 pending action path through server actions.
- The UI shows persistence metadata from the list endpoint:
  - `storeMode`
  - `firestorePersistence`
  - `previewOnly`
  - `safetyMode`
- Server actions preserve non-2xx v2 response bodies so the UI can keep showing
  confirmed/cancelled state after a 409 conflict.
- Terminal states do not show active confirm/cancel controls.

Docs:

- `docs/phase9_personal_agent_v2_release_gate.md` contains schema, security
  model, DI switch plan, rollback plan, and release gate steps.
- `docs/phase9_2_firestore_persistence_preview_enablement_runbook.md` contains
  the approved-preview execution sequence, env checklist, rollback steps, and
  smoke command template.
- `docs/personal_agent_v2_firestore_persistence_enablement_result.md` records
  the deployed API revision, env changes, service smoke, and user-verified
  authenticated persistence smoke.
- `scripts/smoke-personal-agent-v2-persistence.mjs` is available for the
  approved release gate. It does not run mutating authenticated checks unless
  `RUN_PERSONAL_AGENT_V2_PERSISTENCE_SMOKE=true` and a Firebase ID token are
  provided.

## Firestore Persistence Status

Enabled path:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Behavior:

- Reads and writes are scoped under the authenticated user path.
- Record readback checks `userSubjectRef`.
- Payload snapshot is stored and preserved.
- Audit refs, validation snapshot, redaction metadata, timestamps, and status
  fields are serialized. Top-level time fields and `audit.updatedAt` are
  serialized as Firestore `Timestamp` values.
- `executed=false` and `wroteData=false` are forced by serialization and
  readback.
- Duplicate document creation with a different idempotency key is rejected
  instead of replacing an existing pending action.
- Direct `executed` transition is rejected before write.
- Owner-checked status and metadata mutations run inside Firestore transactions
  in the durable candidate, avoiding stale read-then-set overwrites during
  concurrent confirm/cancel requests.
- Confirmed, cancelled, expired, rejected, blocked, and executed states are
  finalized for Personal Agent v2 state memory and cannot receive late status,
  metadata, or guard-decision mutations.
- The pending action Firestore resolver is lazy. Default in-memory and rollback
  modes do not resolve `FirestoreDb` for the Personal Agent v2 store.
- The in-memory fallback uses owner-scoped keys, so local and rollback behavior
  follows the same ownership model as
  `users/{userId}/pendingActions/{pendingActionId}`.

Enabled:

- API env selects Firestore persistence:
  - `AGENT_PENDING_ACTION_STORE_MODE=firestore`
  - `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true`
  - `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true`
- API revision `life-agent-api-00042-zhr` serves 100% traffic.
- Firestore pending action writes are limited to preview-only state memory under
  `users/{userId}/pendingActions/{pendingActionId}`.
- User browser smoke verified refresh restore of confirmed and cancelled
  pending action history.

Not enabled:

- `life_events`
- `memories`
- real tool execution
- external provider execution
- frontend direct Firestore access

## Remaining Follow-up

No blocker remains for the main Personal Agent v2 user-visible state-memory
path. The remaining follow-up is:

1. Run deployed cross-user owner-isolation smoke with a second Firebase test
   account or second Firebase ID token.
2. Optionally run the scripted authenticated smoke once a Firebase ID token is
   available to supplement the user-provided browser evidence.

## Risk Review

Low risk locally:

- The default mode remains in-memory.
- Rollback modes select in-memory, including
  `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=false`.
- Invalid env-style persistence values fail safe to in-memory.
- The pending action store does not resolve `FirestoreDb` unless all persistence
  gates select the Firestore candidate.
- The v2 route is separate from legacy `/api/agent/confirm`.
- No real write flags are enabled by code changes.
- Firestore persistence requires all three gates:
  `AGENT_PENDING_ACTION_STORE_MODE=firestore`,
  `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true`, and
  `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true`.

Remaining production risks:

- Cloud Run env could accidentally combine persistence enablement with legacy
  write flags if not checked before deployment.
- Service account/IAM scope remains broad and should be narrowed in a separate
  IAM hardening task.
- Deployed cross-user smoke still needs a second test user.

## Do Not Change Without Approval

- Cloud Run env, except for an explicitly approved rollback of the three
  pending-action persistence vars
- IAM
- `firestore.rules`
- `firebase.json`
- `life_events`
- `memories`
- real tool executor
- external provider execution
- production deployment

## Recommended Next Step

Proceed to:

```text
Personal Agent v2 closeout, then Phase 6 Memory Engine planning
```

The main Personal Agent v2 state-memory path is now deployed and user-verified.
Before starting Memory Engine writes, keep real tool execution and business data
writes behind their existing release gates.

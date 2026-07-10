# Phase 9 Personal Agent v2 Release Gate

## Executive Summary

Personal Agent v2 now has a safe persistence foundation: the runtime depends on
`IPendingActionStore`, defaults to `InMemoryPendingActionStore`, and has a
candidate `FirestorePendingActionStore` for durable pending action state. Real
Firestore persistence is not enabled in this phase.

Release gate decision: **NOT YET ENABLED**. Enabling real persistence requires a
separate approval to change Cloud Run env and deploy.

## Current Architecture

Current preview path:

```text
Agent Preview UI
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> InMemoryPendingActionStore by default
```

Approved future persistence path:

```text
Agent Preview UI
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> FirestorePendingActionStore when explicitly gated
  -> users/{userId}/pendingActions/{pendingActionId}
```

Compatibility route:

- `/api/agent/pending-actions/demo` remains as a Phase 8 deployment alias.
- New frontend calls use `/api/agent/pending-actions`.
- Both routes use the same Personal Agent v2 runtime and `IPendingActionStore`.

The list response includes persistence metadata even when no actions exist:

- `storeMode`
- `firestorePersistenceEnabled`
- `previewOnly`
- `safetyMode`

Mainline pending action contract:

- `IPendingActionStore`
- `PendingActionRecord`
- `InMemoryPendingActionStore`
- `FirestorePendingActionStore`
- `Phase80PendingActionRuntime`

Legacy `PendingAgentAction` / `IPendingAgentActionStore` remains only for the
old `/api/agent/confirm` path and is not the Personal Agent v2 persistence
route.

## Firestore Schema

Collection path:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Document fields:

- `pendingActionId`
- `userId`
- `userSubjectRef`
- `previewId`
- `confirmationId`
- `toolId`
- `toolVersion`
- `adapterId`
- `actionType`
- `sessionSubjectRef`
- `riskLevel`
- `status`
- `payload`
- `createdAt`
- `updatedAt`
- `confirmedAt`
- `cancelledAt`
- `expiresAt`
- `idempotencyKeyHash`
- `inputHash`
- `previewHash`
- `policySnapshotRef`
- `traceId`
- `auditEventRefs`
- `audit`
- `sanitizedPreviewRef`
- `serverOnlyPayloadRef`
- `redactionMetadata`
- `validationSnapshot`
- `blockedReason`
- `cancellationReason`
- `schemaVersion`
- `wroteData`
- `executed`

Safety invariant: `wroteData=false` and `executed=false` are forced by the
store serializer, even if an input record accidentally contains true values.

## Security Model

- User identity comes from `FirebaseAuthMiddleware` via `HttpContext.Items["userId"]`.
- Request body cannot set or override `userId`.
- Store reads and writes are scoped to
  `users/{userId}/pendingActions/{pendingActionId}`.
- `FirestorePendingActionStore` verifies `userSubjectRef` after document read.
- Cross-user access returns not found instead of leaking ownership.
- Confirm uses the existing stored payload snapshot and does not accept a new
  payload.
- Cancelled, expired, rejected, blocked, or executed terminal records cannot be
  moved back into a mutable state.
- Store implementations reject direct `executed` status transitions. Execution
  remains a separate future runtime and is not part of Personal Agent v2.
- `PendingActionTransitionPolicy` centralizes shared status validation for both
  in-memory and Firestore store implementations.
- `FirestorePendingActionStore` performs owner-checked status and metadata
  mutations inside Firestore transactions so future concurrent confirm/cancel
  requests cannot overwrite each other with stale read-then-set writes.
- Terminal records cannot receive late confirmation metadata or guard-decision
  mutations; this keeps confirmed/cancelled/expired history immutable for the
  Personal Agent v2 state memory path.
- Confirmed is not executed. Real tool execution is still unavailable.

## IAM Requirements

Before enabling Firestore persistence in Cloud Run, confirm the API service
account has only the Firestore permissions needed for:

- read documents under `users/{userId}/pendingActions`
- create documents under `users/{userId}/pendingActions`
- update status fields for documents under `users/{userId}/pendingActions`

Do not broaden IAM for:

- `users/{userId}/memories`
- `users/{userId}/life_events`
- Cloud Storage write paths
- tool execution infrastructure

## DI Switch Plan

The production DI candidate is present but defaults safe.
`PendingActionStoreFactory` owns the runtime store selection so the release
gate can test the switch independently from `Program.cs`.
The Firestore client resolver is lazy for the pending action store: default
in-memory and rollback modes do not resolve `FirestoreDb` for Personal Agent v2
pending action persistence.

Default:

```text
AGENT_PENDING_ACTION_STORE_MODE unset
AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE unset
=> InMemoryPendingActionStore
```

Future approved persistence enablement:

```text
AGENT_PENDING_ACTION_STORE_MODE=firestore
AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true
AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true
=> FirestorePendingActionStore
```

`AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY` must remain true for Personal Agent
v2. It does not enable real tool execution and must not be paired with write
flags for `life_events` or `memories`. The store factory treats
`AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=false` as a safe rollback condition:
even with Firestore mode and approval enabled, it selects
`InMemoryPendingActionStore` instead of durable persistence.

Rollback modes:

- unset all pending-action persistence env vars
- or set `AGENT_PENDING_ACTION_STORE_MODE=in_memory`
- or set `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=false`
- or set `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=false`

All rollback modes select `InMemoryPendingActionStore`.

## Test Requirements

Before release gate approval:

1. `git diff --check`
2. `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
3. `npm --prefix life-agent-web run lint` if the frontend changed
4. Confirm tests cover:
   - create
   - read after runtime refresh
   - confirm persistence
   - cancel persistence
   - owner isolation
   - invalid transition
   - cancelled cannot confirm
   - confirmed is not executed
   - direct `executed` status transition is rejected
   - terminal records reject late metadata and guard mutations
   - shared transition policy rejects unsafe status changes
   - payload is not modified by confirm
   - historical confirmed and cancelled records remain listable
   - store factory defaults and rollback modes select in-memory
   - store factory selects Firestore only when mode and approval are explicit
   - list endpoint exposes persistence metadata without requiring an action
   - Firestore schema serialization keeps execution flags false
   - Firestore schema readback preserves payload, audit refs, and safety fields

Before production traffic:

1. Deploy preview-only revision.
2. Run unauthenticated smoke.
3. Run authenticated smoke with a real Firebase ID token.
4. Create pending action.
5. Confirm the UI/API reports `firestorePersistenceEnabled=true`.
6. Refresh page and confirm the action remains visible.
7. Confirm the action and refresh again.
8. Cancel a separate action and refresh again.
9. Confirm no `life_events`, `memories`, or real tool execution occurred.

## Deployment Steps

Deployment requires a separate explicit approval.

1. Confirm git status is clean.
2. Confirm Cloud Run env currently has no dangerous write flags:
   - `ENABLE_AGENT_WRITE_TOOLS` is unset or false
   - `ENABLE_CREATE_LIFE_EVENT_TOOL` is unset or false
3. Confirm Firebase/Auth smoke can be performed.
4. Change only pending action persistence env:
   - `AGENT_PENDING_ACTION_STORE_MODE=firestore`
   - `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true`
   - `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true`
5. Deploy API preview revision.
6. Deploy Web only if the UI changed.
7. Run authenticated persistence smoke.
8. Keep real tool execution disabled.

## Rollback Plan

Rollback options:

- Set `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=false`.
- Or set `AGENT_PENDING_ACTION_STORE_MODE=in_memory`.
- Or shift Cloud Run traffic back to the previous known-good revision.

Rollback does not require data cleanup because pending action records are
preview-only and do not write `life_events` or `memories`.

## Do Not Change Without Approval

- Cloud Run env
- IAM
- `firestore.rules`
- `firebase.json`
- `life_events`
- `memories`
- real tool executor
- external provider execution
- production deployment

## Current Conclusion

Personal Agent v2 is ready for a Firestore persistence release gate review, but
real persistence is not enabled. The next phase should be a focused release
gate run that explicitly approves the Cloud Run env switch and authenticated
persistence smoke.

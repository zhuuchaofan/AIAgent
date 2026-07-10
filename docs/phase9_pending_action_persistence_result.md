# Phase 9 Pending Action Persistence Result

Date: 2026-07-10

## 1. Executive summary

Phase 9 added the Pending Action persistence foundation without enabling real
Firestore persistence in production. The Phase 8 Personal Agent Preview runtime
now uses the Phase 7 `IPendingActionStore` line by default through an
in-memory store, and a Firestore-backed candidate store exists for a future
release gate.

## 2. Current implementation

Current deployed behavior remains safe by default:

```text
AgentPreview.tsx
  -> /api/agent/pending-actions/demo
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> InMemoryPendingActionStore
```

This preserves the Phase 8 user-visible loop:

- create pending action
- list pending actions
- confirm
- cancel
- confirmed is not executed
- no real tool action
- no `life_events` write
- no `memories` write

The in-memory store is now a real application service class rather than a test
fixture. A future runtime can swap in a different `IPendingActionStore`
implementation without changing the front-end contract.

## 3. Store architecture

The canonical future store interface is still:

```text
LifeAgent.Api/Services/Agent/PendingActions/IPendingActionStore.cs
```

No new pending action store abstraction was introduced.

Phase 9 implementation files:

- `InMemoryPendingActionStore`
- `FirestorePendingActionStore`
- `PendingActionRecord.Payload`
- `PendingActionCreateRequest.Payload`
- `Phase80PendingActionRuntime` optional `IPendingActionStore` integration

The legacy `IPendingAgentActionStore` path remains separate and unchanged.

## 4. Firestore schema

The Firestore candidate writes to the user-scoped path:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Candidate document fields include:

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
- `wroteData=false`
- `executed=false`

The store always reads and writes through the authenticated user-scoped path.
Cross-user lookups return not found.

## 5. Safety invariants

Phase 9 preserves these invariants:

- `userId` comes from authenticated API context, not request body.
- store owner checks are required for read, confirm, cancel, and query.
- cancelled actions cannot be confirmed.
- confirmed actions are not executed.
- `executed=false` is preserved.
- `wroteData=false` is preserved.
- no executor is invoked by pending action persistence.
- no `life_events` or `memories` write is introduced.
- no production Firestore persistence is enabled by default.

## 6. Feature flag / enablement status

No new Cloud Run env flag was added.

Production enablement is intentionally not wired:

- `Program.cs` does not register `FirestorePendingActionStore` for production.
- Cloud Run env was not modified.
- `firestore.rules` was not modified.
- `firebase.json` was not modified.
- no collection was created.
- no deployment was performed.

The current safe default remains in-memory persistence. Enabling Firestore
persistence requires a separate release gate.

## 7. Test results

Local tests added or extended cover:

1. create then read/list through shared store
2. refresh-style restore through a new runtime instance sharing the same store
3. confirm status persists
4. cancel status persists
5. cancelled cannot confirm
6. cross-user access is blocked
7. request body cannot set owner user id
8. confirmed is not executed
9. real executor is not called
10. Firestore candidate schema serializes required fields with
    `executed=false` and `wroteData=false`

Verification command:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

Result:

```text
Passed: 382
Failed: 0
Skipped: 0
```

## 8. Not done in Phase 9

Phase 9 did not:

- enable real Firestore persistence
- connect production DI to `FirestorePendingActionStore`
- deploy
- modify Cloud Run env
- modify `firestore.rules`
- modify `firebase.json`
- create `users/{userId}/pendingActions`
- write real production pending action data
- write `users/{userId}/memories`
- write `life_events`
- execute real tools
- call external provider APIs
- install dependencies

## 9. Remaining Firestore Persistence Release Gate

Before real persistence can be enabled:

- approve Cloud Run env / feature gate shape
- approve production DI switch from in-memory to Firestore store
- confirm Cloud Run service account and IAM scope
- approve Firestore collection path and schema
- run emulator or isolated test project validation
- verify owner-only reads and writes
- verify `confirmed != executed`
- verify no executor is reachable from confirm
- verify no `memories` or `life_events` writes
- update deployment and rollback plan
- perform authenticated preview smoke after deployment

Recommended next phase:

```text
Phase 9.1 Firestore Persistence Release Gate Review
```

Do not enable production Firestore persistence until that gate is explicitly
approved.

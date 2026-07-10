# Personal Agent v2 Readiness Audit

Date: 2026-07-10

## Executive Summary

Personal Agent v2 is locally ready for a Firestore persistence release gate, but
it is not complete in production until Firestore-backed pending action
persistence is explicitly enabled, deployed, and authenticated-smoke verified.

Current decision: **CONDITIONAL GO for release gate review; NO-GO for claiming
Personal Agent v2 complete in production.**

## Current Runtime Map

Current default runtime:

```text
AgentPreview.tsx
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> PendingActionStoreFactory
  -> InMemoryPendingActionStore
```

Approved future persistence route:

```text
AgentPreview.tsx
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> PendingActionStoreFactory
  -> FirestorePendingActionStore
  -> users/{userId}/pendingActions/{pendingActionId}
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
| Create pending action | `Phase80PendingActionRuntime.CreateAsync`; `/api/agent/pending-actions`; tests in `Phase80PendingActionRuntimeMvpTest` and `Phase9PendingActionPersistenceTest` | Locally ready |
| Refresh restores state | Works when the same store is shared; `StoreCreateCanBeReadAfterRuntimeRefresh` covers runtime refresh. Cross-instance restore requires Firestore enablement | Partial |
| View historical pending actions | `ListAsync` returns all user-scoped records, including confirmed and cancelled; `ListReturnsHistoricalConfirmedAndCancelledActions` covers this | Locally ready |
| Confirm persists status | `ConfirmStatusPersistsAndDoesNotExecute` covers status persistence through shared store | Locally ready |
| Cancel persists status | `CancelStatusPersistsAndCannotConfirm` covers status persistence through shared store | Locally ready |
| User can only access own data | `CrossUserAccessIsBlocked`; endpoint owner comes from auth context | Locally ready |
| Status transitions are safe | `PendingActionTransitionPolicy`; `TransitionPolicyRejectsUnsafeStatusChanges`; direct `executed` transition rejected | Locally ready |
| Agent does not auto execute | `executed=false`, `wroteData=false`, `executionReady=false`; store serializers force false; tests cover no execution | Locally ready |
| Firestore persistence enabled | Requires Cloud Run env change and deployment approval | Blocked by release gate |
| Production smoke proves refresh restore | Requires deployed Firestore persistence and authenticated browser/API smoke | Blocked by release gate |

## Local Implementation Evidence

Backend:

- `IPendingActionStore` is the mainline store contract.
- `InMemoryPendingActionStore` is the default safe store.
- `FirestorePendingActionStore` is the durable candidate.
- `PendingActionStoreFactory` owns mode selection.
- `PendingActionPersistenceOptions` requires explicit Firestore approval.
- `PendingActionTransitionPolicy` centralizes status transition checks.
- `AgentEndpoints` exposes `/api/agent/pending-actions` as the v2 path.

Frontend:

- `AgentPreview.tsx` uses the v2 pending action path through server actions.
- The UI shows persistence metadata from the list endpoint:
  - `storeMode`
  - `firestorePersistence`
  - `previewOnly`
  - `safetyMode`
- Terminal states do not show active confirm/cancel controls.

Docs:

- `docs/phase9_personal_agent_v2_release_gate.md` contains schema, security
  model, DI switch plan, rollback plan, and release gate steps.

## Firestore Persistence Candidate Status

Candidate path:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Candidate behavior:

- Reads and writes are scoped under the authenticated user path.
- Record readback checks `userSubjectRef`.
- Payload snapshot is stored and preserved.
- Audit refs, validation snapshot, redaction metadata, timestamps, and status
  fields are serialized.
- `executed=false` and `wroteData=false` are forced by serialization and
  readback.
- Direct `executed` transition is rejected before write.

Not enabled:

- No Cloud Run env changed.
- No Firestore collection created.
- No production write performed.
- No deployment performed.

## Release Gate Blockers

The following cannot be completed without explicit approval:

1. Modify Cloud Run env to select Firestore persistence.
2. Deploy the API revision that uses Firestore persistence.
3. Run authenticated smoke against the deployed service.
4. Verify refresh restores state across browser reload and Cloud Run instance
   changes.
5. Confirm `firestorePersistenceEnabled=true` in UI/API.
6. Confirm no `life_events`, `memories`, or real tool execution occurs.

## Risk Review

Low risk locally:

- The default mode remains in-memory.
- Rollback modes select in-memory.
- The v2 route is separate from legacy `/api/agent/confirm`.
- No real write flags are enabled by code changes.

Remaining production risks:

- Cloud Run env could accidentally combine persistence enablement with legacy
  write flags if not checked before deployment.
- Service account/IAM scope must be verified before Firestore persistence.
- Authenticated smoke is required to prove owner isolation and refresh restore
  in the deployed environment.

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

## Recommended Next Step

Proceed to:

```text
Phase 9.1 Firestore Persistence Release Gate Review
```

The gate should be read-only first, then require explicit approval before any
Cloud Run env change, deployment, or real Firestore persistence smoke.

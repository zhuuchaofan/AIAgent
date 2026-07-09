# Personal Agent Preview v1 Implementation Result

## Executive summary

Personal Agent Preview v1 is now prepared as a fake-first, user-visible
pending action confirmation loop. It remains preview-only: confirmation changes
state, but does not execute tools, write Firestore pending actions, write
`memories`, or write `life_events`.

## Completed in this pass

- Hardened the Agent Preview pending action UI so terminal states are no
  longer actionable.
- Added a current-instance refresh path for Phase 8 demo pending actions.
- Added clear UI copy that the preview is in-memory and may lose state after a
  service restart or instance switch.
- Kept the Personal Preview v1 mainline on the existing Phase 8 demo path:
  `/api/agent/pending-actions/demo`.
- Added endpoint-level tests for unauthenticated requests and no-execution
  confirmation invariants.
- Documented that Firestore persistence is not enabled and needs a separate
  release gate.

## Current user-visible capability

Logged-in users can use the Agent Preview section to:

1. Generate a pending action.
2. See its current status.
3. Confirm a pending action.
4. Cancel a pending action.
5. Refresh the current API instance's in-memory pending action list.
6. See that confirmed actions are "confirmed, not executed".
7. See that confirmed or cancelled actions no longer expose active
   confirm/cancel controls.
8. See explicit safety fields:
   - `executed=false`
   - `wroteData=false`
   - `legacyConfirm=false`
   - `realWritePath=false`
   - `guard=deny_all_no_real_execution`

## Pending action mainline

Personal Agent Preview v1 uses the Phase 8 in-memory demo runtime:

```text
AgentPreview.tsx
  -> createPhase80PendingAction / listPhase80PendingActions
  -> confirmPhase80PendingAction / cancelPhase80PendingAction
  -> /api/agent/pending-actions/demo
  -> static Phase80PendingActionRuntime
```

This path remains deliberately separate from the legacy Agent Preview
confirmation path:

```text
AgentPreview.tsx legacy proposedAction panel
  -> confirmAgentAction
  -> /api/agent/confirm
  -> IPendingAgentActionStore
  -> optional create_life_event write coordinator behind write flags
```

No fourth pending action model or store abstraction was added.

## Why state can still disappear

The current runtime is still `Phase80PendingActionRuntime`, stored in the API
process memory. Refreshing the page can restore actions from the same API
instance, but state can disappear when:

- the Cloud Run instance restarts;
- traffic is routed to a different instance;
- a new revision replaces the running process;
- the in-memory dictionary is empty for that user.

This is now shown in the UI. Durable state requires a separately approved
Firestore persistence release gate.

## Backend safety invariants

The Phase 8 demo endpoints still:

- read `userId` only from the authenticated server context;
- reject missing `userId` with 401;
- do not accept `userId` from the request body;
- do not call legacy `/api/agent/confirm`;
- do not call `IPendingAgentActionStore`;
- do not call `FirestorePendingAgentActionStore`;
- do not call `AgentLifeEventConfirmationWriteCoordinator`;
- keep `confirmed != executed`;
- keep `executed=false`;
- keep `wroteData=false`;
- keep `executionReady=false`;
- return `legacyConfirmEndpointUsed=false`;
- return `realWritePath=false`.

## Still not implemented

- Durable Firestore pending action persistence.
- `FirestorePendingActionStore` production implementation or DI wiring.
- Production pending action collection creation.
- Real tool execution.
- Real `life_events` write from the Phase 8 demo path.
- Real `users/{userId}/memories` write.
- Cloud Run deployment for this change.
- Cloud Run environment variable changes.

## Firestore persistence readiness

The repository already has server-side Firestore dependencies and a Phase 7
`IPendingActionStore` contract track, so a future Firestore-backed candidate is
technically feasible without adding a new package. It is not enabled in this
phase because persistence would introduce a real Firestore write path and
requires an explicit release gate.

Before implementing or enabling Firestore persistence, approve:

- canonical mapping between Phase 8 preview records and Phase 7
  `IPendingActionStore` records;
- whether to replace or bridge the Phase 8 in-memory runtime;
- collection path for `users/{userId}/pendingActions`;
- Firestore rules / emulator coverage;
- Cloud Run service account access expectations;
- no-production-DI dry run;
- deployment and rollback plan;
- authenticated smoke test plan;
- confirmation that legacy `/api/agent/confirm` write flags remain disabled.

## Deployment readiness

This change is ready for a preview-only deployment gate review, subject to the
normal checks:

- backend tests pass;
- frontend lint passes;
- Cloud Run env flags are read-only verified before deployment;
- `ENABLE_AGENT_WRITE_TOOLS` is not `true`;
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is not `true`;
- no deploy command uses env mutation flags.

This change is not a Firestore persistence release.

## Next recommendation

Proceed with a small preview deployment gate for Personal Agent Preview v1.
After that, plan a focused Firestore persistence release gate instead of adding
more docs-only subphases. The likely next implementation step is to bridge the
Phase 8 preview runtime to the Phase 7 `IPendingActionStore` contract behind a
test-only or disabled-by-default adapter, then promote it only after emulator
and authorization tests pass.

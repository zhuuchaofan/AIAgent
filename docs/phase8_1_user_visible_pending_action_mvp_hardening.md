# Phase 8.1 User-visible Pending Action MVP Hardening

Date: 2026-07-09

## What This Phase Changed

Phase 8.1 hardens the Phase 8.0 fake-first Pending Action MVP without changing
the production Firestore path, Cloud Run configuration, or real execution
behavior.

Changes:

- Phase 8 demo pending action responses now explicitly declare:
  - `safetyMode = phase8_fake_first_in_memory`
  - `legacyConfirmEndpointUsed = false`
  - `realWritePath = false`
- the Agent Preview UI now displays these safety fields for the Phase 8 demo
  card
- UI copy now labels the Phase 8 area as a safe pending-action demo where
  confirm only changes state
- the legacy Agent Preview proposed-action card now states that it is the older
  confirm path and requires deployment flag review
- tests now assert that confirmed Phase 8 demo actions remain not executed and
  do not use the legacy confirm or real-write path

## Phase 8 Demo Path vs Legacy PendingAgentAction Path

Phase 8 demo path:

```text
AgentPreview.tsx
  -> createPhase80PendingAction / confirmPhase80PendingAction / cancelPhase80PendingAction
  -> /api/agent/pending-actions/demo
  -> Phase80PendingActionRuntime
  -> in-memory only
```

Legacy Agent Preview path:

```text
AgentPreview.tsx
  -> runAgentPreview / confirmAgentAction
  -> /api/agent/run and /api/agent/confirm
  -> AgentRunner / IPendingAgentActionStore
  -> FirestorePendingAgentActionStore in production DI
```

The Phase 8 demo confirm and cancel paths do not call `/api/agent/confirm`.
They do not call `IPendingAgentActionStore`, `FirestorePendingAgentActionStore`,
or `AgentLifeEventConfirmationWriteCoordinator`.

## UI Copy Hardening

The Phase 8 demo UI now emphasizes:

- this is a待确认动作演示
- current mode is safe demo mode
- confirmation only changes lifecycle status
- confirmed means `已确认，尚未执行`
- the demo does not write real data
- `executed`, `wroteData`, and `realWritePath` remain false

The old Agent Preview confirmation card is also labeled as the older confirm
path, so it is less likely to be confused with the Phase 8 demo path.

## Legacy `/api/agent/confirm` Risk

The legacy `/api/agent/confirm` risk still exists by design: if both
`ENABLE_AGENT_WRITE_TOOLS` and `ENABLE_CREATE_LIFE_EVENT_TOOL` are enabled, the
legacy confirm path can enter the `create_life_event` write coordinator.

Phase 8.1 reduces confusion but does not remove or rewrite that path.

Current mitigation:

- defaults remain closed unless both flags are explicitly enabled
- Phase 8 demo UI no longer uses `confirmAgentAction`
- Phase 8 demo API is separate from `/api/agent/confirm`
- Phase 8 demo responses expose `legacyConfirmEndpointUsed = false`
- documentation records the deployment blocker

Deployment must still confirm that both real-write flags are unset or false.

## Safety Invariants

Maintained invariants:

- authenticated `userId` comes from `HttpContext.Items["userId"]`
- Phase 8 create request does not accept `userId`
- confirm/cancel do owner lookup by authenticated user
- confirm does not accept payload resubmission
- `confirmed` is not `executed`
- `executionReady` remains false
- `executed` remains false
- `wroteData` remains false
- `realWritePath` remains false
- no real tool executor is called

## Real Writes / Execution

This phase did not write:

- `users/{userId}/pendingActions`
- `users/{userId}/memories`
- `users/{userId}/life_events`
- legacy `agent_pending_actions` through the Phase 8 demo path

This phase did not execute real tool actions and did not call external provider
APIs.

## Configuration / Deployment

This phase did not modify:

- Cloud Run env
- production Firebase config
- `firestore.rules`
- `firebase.json`
- package files / lockfiles
- production DI

No deployment was performed.

## Deployment Blockers

Before deploying this UI/API surface, confirm:

1. Whether Phase 8 demo endpoints should be exposed in production.
2. Whether `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` should expose both legacy Agent
   Preview and Phase 8 demo UI.
3. `ENABLE_AGENT_WRITE_TOOLS` is false/unset.
4. `ENABLE_CREATE_LIFE_EVENT_TOOL` is false/unset.
5. `USE_MOCK_AUTH` is not enabled.
6. Existing legacy `agent_pending_actions` writes are acceptable if legacy Agent
   Preview remains visible.
7. UI copy is acceptable for preview/demo exposure.

## Next Phase Recommendation

Recommended next phase:

```text
Phase 8.2 Preview-only Deployment Gate Review
```

Only choose this if the user wants to deploy. Otherwise continue with a small
Phase 8.2 local hardening task:

- split Agent Preview UI sections more cleanly
- add endpoint-level tests for Phase 8 demo auth behavior
- keep fake-first runtime
- do not connect Firestore
- do not enable real execution

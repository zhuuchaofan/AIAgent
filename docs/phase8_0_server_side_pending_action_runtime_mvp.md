# Phase 8.0 Server-side Pending Action Runtime MVP

Date: 2026-07-09

## What This Phase Implements

Phase 8.0 starts the server-side Pending Action Runtime with a fake-first,
user-visible confirmation loop.

Implemented loop:

1. authenticated user opens Agent Preview
2. user clicks `生成待确认动作`
3. API creates a fake pending action in process memory
4. frontend displays the pending action
5. user can confirm or cancel
6. confirm changes status to `confirmed`
7. cancel changes status to `cancelled`
8. confirmed actions remain not executed
9. UI displays `pending`, `confirmed`, `cancelled`, and
   `confirmed but not executed`

## User-visible Capability

The Agent Preview panel now includes a Phase 8.0 Pending Action card. Users can
generate a pending action, inspect its status, confirm it, or cancel it.

The card explicitly shows:

- lifecycle status
- `executed: false`
- `wroteData: false`
- guard decision as `deny_all_no_real_execution`
- confirmation text that confirmed does not mean executed

## Why Fake / In-memory Store

Phase 8.0 intentionally uses a fake in-memory runtime because the goal is to
prove the end-to-end confirmation interaction without crossing a production data
boundary.

The in-memory runtime:

- does not connect Firestore
- does not create collections
- does not persist across process restarts
- does not store server-only executable payloads
- owner-checks all confirm / cancel operations by authenticated user id

This keeps the first user-visible loop low risk while preserving the future
server-side API shape.

## Why Not Real Firestore

Real Firestore pending action storage is still behind a future Release Gate.
Before it can be enabled, the project still needs explicit approval for:

- document schema
- `users/{userId}/pendingActions` path
- service account / IAM boundary
- production DI
- audit and idempotency behavior
- emulator / fake-first regression signoff
- deployment plan

Phase 8.0 does not write `users/{userId}/pendingActions`,
`users/{userId}/memories`, or `life_events`.

## Confirmed Is Not Executed

`confirmed` is only a user decision state. It does not execute tools, write
data, call providers, or mark execution readiness.

The Phase 8.0 response keeps:

- `executed = false`
- `wroteData = false`
- `executionReady = false`
- `guardDecision = deny_all_no_real_execution`

The runtime does not expose an execute endpoint.

## Guard / Execution Boundary

The current phase remains deny-all for real execution. The guard boundary is
represented in the Phase 8.0 response and UI as
`deny_all_no_real_execution`.

No real executor is implemented or called.

## API Shape

The MVP adds a demo-only API path under the existing Agent API group:

```text
POST /api/agent/pending-actions/demo
GET  /api/agent/pending-actions/demo
POST /api/agent/pending-actions/demo/{actionId}/confirm
POST /api/agent/pending-actions/demo/{actionId}/cancel
```

All endpoints use `HttpContext.Items["userId"]` from the verified auth
middleware. The create request does not accept `userId`, and confirm / cancel
do not accept payload resubmission.

## Tests

Local unit coverage verifies:

- create pending action
- confirm pending action
- cancel pending action
- confirmed does not execute
- cancelled cannot confirm
- expired cannot confirm
- cross-user confirm is blocked
- owner-scoped list returns only the current user's actions

## Not Done

Phase 8.0 does not:

- deploy
- modify Cloud Run env
- modify production Firebase configuration
- modify `firestore.rules`
- modify `firebase.json`
- install dependencies
- connect real Firestore
- create Firestore collections
- implement `FirestorePendingActionStore`
- connect production DI for the new `IPendingActionStore`
- execute real tool actions
- call external provider APIs
- process real secrets

## Future Deployment Gate

Cloud Run deployment remains a separate approval gate. Before deploying this
runtime, the project should explicitly review:

- whether demo endpoints should be enabled in production
- auth and rate-limit behavior
- UI copy for preview-only actions
- monitoring and logging fields
- rollback plan
- whether a future persistent store is approved

No deployment was performed in Phase 8.0.

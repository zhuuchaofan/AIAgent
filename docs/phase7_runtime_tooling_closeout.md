# Phase 7 Runtime Tooling Closeout

Date: 2026-07-09

## Summary

Phase 7 Runtime Tooling is closed after Phase 7.8 through Phase 7.19.

This phase established the offline regression fixtures, pending action store
contract, guard runtime skeleton, Firestore emulator / rules planning track, and
server-side authorization plan needed before moving back from docs-only safety
work into a user-visible Pending Action runtime.

## Completed Work

| Phase | Commit | Outcome |
| --- | --- | --- |
| 7.8 Offline Fixture / Mock Tool Regression | `0773620` | Added offline mock regression coverage for fixture-based tool behavior. |
| 7.9 Store Interface Skeleton | `c5cff50` | Added the future `IPendingActionStore` contract and pending action model skeleton. |
| 7.10 Guard Runtime Skeleton | `073b414` | Added guarded execution contracts and deny/block decision skeleton. |
| 7.11 Offline Guard Chain Harness | `e16bd95` | Added offline guard chain fixture coverage with no real execution. |
| 7.12 Store Implementation Plan | `f3063d3` | Documented future store implementation stages and safety gates. |
| 7.13 Firestore Emulator / Rules Test Plan | `487ba2b` | Planned emulator and Firestore Rules test strategy. |
| 7.14 Firestore Emulator Test Skeleton | `f7d98ab` | Added static skeleton and fixture matrix for rules tests. |
| 7.15 Firestore Emulator Test Infrastructure | `f85ded3` | Added test-only rules infrastructure draft. |
| 7.16 Firestore Rules Unit Test Skeleton | `3586178` | Added non-running rules unit test skeleton. |
| 7.17 Firestore Emulator Test Enablement Review | `733cc53` | Reviewed future dependency and enablement approach. |
| 7.18 Firestore Rules Package Plan | `47b8336` | Planned independent `tests/firestore-rules` package boundary. |
| 7.18 Firestore Access Path Decision | `46332ad` | Chose Cloud Run server-side Firestore access as the primary future path. |
| 7.19 Server-side PendingActionStore Authorization Plan | `ad000a4` | Defined server-side owner checks, trust boundary, and release gates. |

## Current Runtime Tooling Architecture

The current runtime tooling line is:

```text
Frontend
  -> Cloud Run API
  -> authenticated user id from middleware
  -> pending action / guard runtime boundary
  -> future server-side store
  -> future Firestore path only after approval
```

Current code contains two related tracks:

- the older `IPendingAgentActionStore` Agent Preview path, currently wired in
  production DI to `FirestorePendingAgentActionStore`
- the newer Phase 7 `IPendingActionStore` contract and guard runtime skeleton,
  not yet wired to production DI

Phase 7 intentionally did not replace the production store or enable a real
execution path.

## Main Safety Boundaries

The primary safety boundary is server-side:

- user id comes from verified API auth context
- request body user id must not be trusted
- owner checks are required before read / confirm / cancel / execute
- `confirmed` is not `executed`
- `execution_ready` is not `executed`
- guard-denied actions cannot become executable
- missing Release Gate blocks real execution
- Firestore Rules are secondary defense-in-depth, not the primary Cloud Run
  enforcement layer

## Firestore Access Path Decision

Phase 7.18 selected Cloud Run API as the future write boundary for pending
action, memory, and execution state. The client should not write pending action
documents directly to Firestore.

The future preferred path remains:

```text
Web Frontend -> Cloud Run API -> server-side authorization -> store -> Firestore
```

## Firestore Rules Track

The Firestore Rules / emulator work from Phase 7.13 through Phase 7.17 remains
valuable, but it is now secondary / defense-in-depth. It should protect against
future client-direct Firestore mistakes and document deny-by-default
expectations, but it is not the main blocker for server-side Pending Action MVP
work.

## Server-side Authorization Plan

Phase 7.19 completed the server-side PendingActionStore authorization plan.
Future implementations must enforce:

- authenticated owner binding
- scoped reads and state transitions
- server-generated timestamps and status
- no client-submitted executable payload on confirm
- audit and idempotency design before real writes
- Release Gate before any real execution or production Firestore write

## Not Implemented In Phase 7

Phase 7 did not implement:

- real `FirestorePendingActionStore`
- production DI for the new `IPendingActionStore`
- real tool execution
- production Firestore collection creation
- writes to `users/{userId}/pendingActions`
- writes to `users/{userId}/memories`
- writes to `life_events`
- Cloud Run deployment
- Cloud Run environment changes
- external provider calls

## Closeout Decision

Phase 7 Runtime Tooling is complete and should stop producing more docs-only
sub-phases by default.

The next phase is:

```text
Phase 8 Server-side Pending Action Runtime
```

Phase 8 starts with a fake-first, user-visible Pending Action confirmation MVP
that proves the interaction loop without connecting real Firestore or executing
real tools.

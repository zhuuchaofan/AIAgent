# Phase 7.11 Offline Guard Chain Harness

Date: 2026-07-09

## 1. Background

Phase 7.8 added offline mock tool fixtures. Phase 7.9 added the pending action
store interface skeleton. Phase 7.10 added the guarded execution runtime
skeleton.

Phase 7.11 connects those three pieces in a local test harness. The harness maps
fixture cases into pending action records, seeds a test-only in-memory store,
and evaluates guard readiness through `GuardedExecutionRuntime`.

## 2. Goals

Phase 7.11 verifies that:

- Phase 7.8 fixture cases can be mapped into Phase 7.9 pending action records
- Phase 7.10 guard runtime can consume those records through `IPendingActionStore`
- low-risk no-write / no-external cases may reach readiness-only
- write intent, external call, high-risk, expired, cancelled, cross-user, stale
  tool version, and hash mismatch cases remain blocked
- `confirmed` does not mean `executed`
- `execution_ready` does not mean `executed`
- guard decision recording updates only status / refs / sanitized reasons
- no response surface exposes server-only payload refs, raw prompts, full
  context, or fake secret-like values

## 3. Non-goals

Phase 7.11 does not:

- execute real tool actions
- call external providers
- send local case or knowledge context to third parties
- connect real Firestore
- create Firestore collections
- modify Firestore Rules
- write `users/{userId}/memories`
- write `life_events`
- register production DI or routes
- deploy
- modify Cloud Run environment variables
- enable MCP
- process or print secrets
- push commits

## 4. Added Harness

Added test file:

```text
LifeAgent.Tests/Phase711OfflineGuardChainTest.cs
```

The test harness uses:

- `LifeAgent.Tests/Fixtures/Phase7_8/offline_mock_tool_regression.json`
- `PendingActionRecord`
- `IPendingActionStore`
- `GuardedExecutionRuntime`
- `DenyAllReleaseGateEvaluator`

The in-memory store is test-only and local to the Phase 7.11 test file. It is
not registered in production dependency injection and is not a Firestore
implementation.

## 5. Decision Mapping

The harness intentionally maps Phase 7.8 fixture cases into the current Phase
7.10 guard runtime semantics:

- low-risk no-write / no-external cases can become `AllowExecutionReady`
- `AllowExecutionReady` is readiness-only and never execution
- write intent without a release gate becomes `RejectWriteIntent`
- external calls without a release gate become `RejectExternalCall`
- high-risk release-gated actions remain blocked
- expired actions become `RejectExpired`
- cancelled actions become `RejectCancelled`
- cross-user access becomes `RejectCrossUser`
- stale tool versions become `RejectToolVersionMismatch`
- input / preview hash mismatches become `RejectHashMismatch`

This keeps Phase 7.8 fixture intent while validating the newer runtime contract.

## 6. Safety Boundary

The harness is deterministic and offline:

- no network
- no provider API
- no real Firestore
- no deployment
- no production write
- no secrets
- no MCP
- no environment mutation

The guard runtime may record `ExecutionBlocked`, `Expired`, or `ExecutionReady`
into the fake store. It never records `Executed`, never sets `WroteData`, and
never marks an external call as made.

## 7. Future Stage Relationship

Recommended next safe phase:

- Phase 7.12 Offline Store Implementation Plan: design Firestore collection
  shape, indexes, TTL, Rules, migration, and rollback as docs-only, without
  creating collections or modifying Rules.

Higher-risk work remains gated by explicit approval:

- real Firestore implementation
- Cloud Run env changes
- Firestore Rules changes
- production DI / routing hookup
- external provider pilots
- MCP
- real execution path
- real write canary

Final conclusion: Phase 7.11 adds an offline integration harness for the Phase
7 runtime tooling chain. It does not execute actions, write production data,
call providers, deploy, modify env, modify Rules, enable MCP, or push.

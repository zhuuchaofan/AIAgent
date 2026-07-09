# Phase 7.9 Store Interface Skeleton

Date: 2026-07-09

## 1. Background

Phase 7.6 defined the Pending Action Store logical model. Phase 7.7 defined the
guarded execution design. Phase 7.8 added offline fixture / mock tool
regression coverage.

Phase 7.9 adds a minimal local pending action store interface skeleton and
contract model so future runtime work can depend on explicit storage semantics
without binding to Firestore or enabling execution.

## 2. Goals

Phase 7.9 adds:

- `IPendingActionStore` skeleton
- pending action status constants
- pending action contract model
- create / update / query request and result DTOs
- a test-only in-memory fake implementation
- offline xUnit tests for create, read, active query, expiration,
  cancellation, idempotency, user scoping, client-safe projection, audit refs,
  and no-execution invariants

## 3. Non-goals

Phase 7.9 does not:

- connect real Firestore
- create Firestore collections
- modify Firestore Rules
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- deploy
- modify Cloud Run environment variables
- enable MCP
- call external APIs
- process real secrets
- push commits

The new interface is not registered in production DI and does not replace the
existing Agent confirmation store.

## 4. Added Skeleton

Added API-side contract files under:

```text
LifeAgent.Api/Services/Agent/PendingActions/
```

Files:

- `IPendingActionStore.cs`
- `PendingActionRecord.cs`
- `PendingActionStatus.cs`
- `PendingActionStoreContracts.cs`

The interface supports:

- create pending action
- get by pending action id
- active query by user
- guarded status update with expected status
- mark expired
- cancel
- record confirmation reference
- record guard decision reference
- check idempotency key hash
- query by preview id
- query by confirmation id
- generic query by safe refs

All methods are async and accept cancellation tokens to match existing service
style.

## 5. Phase 7.6 Alignment

`PendingActionRecord` includes the Phase 7.6 core fields:

- pending action id
- preview id
- confirmation id
- tool id / version
- adapter id
- action type
- user and session subject refs
- risk level
- status
- created / updated / expires timestamps
- idempotency key hash
- input hash
- preview hash
- policy snapshot ref
- trace id
- audit event refs
- sanitized preview ref
- server-only payload ref
- redaction metadata
- validation snapshot
- blocked / cancellation reasons
- schema version

The model deliberately stores refs and hashes rather than raw secrets, prompts,
full context, provider payloads, executable payloads, or Firestore document
bodies.

## 6. Phase 7.8 Alignment

The new tests continue the Phase 7.8 offline style:

- deterministic fake data
- no network
- no real Firestore
- no environment secrets
- no provider calls
- no production data writes
- no execution path

The tests assert that `confirmed` is not `executed` and `execution_ready` is
still not `executed`.

## 7. Fake Store Boundary

The in-memory fake lives only in `LifeAgent.Tests`.

It is intentionally limited:

- not registered in production DI
- not durable
- not Firestore-backed
- not thread-safety hardened beyond simple concurrent dictionary use
- not an execution coordinator
- not a source of production authorization

Its purpose is to test the Phase 7.9 contract semantics offline.

## 8. Why No Firestore

Firestore collection naming, indexes, Rules, migrations, TTL cleanup, and
production rollout require separate review and user approval. Phase 7.9 avoids
all Firestore changes so the work remains local and no-write.

## 9. Future Stage Relationship

Recommended next steps:

- Phase 7.10 Guard Runtime Skeleton: only after user approval, add a no-write
  guard evaluator skeleton that consumes the store interface and returns blocked
  or future-readiness decisions.
- Phase 7.11 Store Implementation Design: only after user approval, design a
  concrete store implementation and migration / rules plan.
- Phase 8 Release Gate / Online Canary: future planning only. Production
  writes, Firestore changes, env changes, provider pilots, MCP, and external
  calls require explicit approval.

Release Gate approval is required before any real store, real execution, or
production write enablement.

## 10. Verification

Run:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --filter Phase79`
- `git diff --stat`
- `git diff --check`
- `git status --short`
- `git log --oneline -4`

Do not run real smoke tests, provider pilots, external API calls, tests
requiring real secrets, tests requiring real Firestore, deploy commands, or
gcloud write commands.

Final conclusion: Phase 7.9 adds a local pending action store contract skeleton
and offline tests. It does not connect Firestore, create collections, modify
Rules, write data, execute tools, call providers, deploy, modify env, enable
MCP, or push.

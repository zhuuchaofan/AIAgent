# Phase 7.10 Guard Runtime Skeleton

Date: 2026-07-09

## 1. Background

Phase 7.7 defined Guarded Execution Design. Phase 7.9 added a pending action
store interface skeleton. Phase 7.10 adds a local guard runtime skeleton that
can evaluate future execution readiness without executing actions.

This phase remains local and no-write. It does not connect Firestore, create
collections, modify Firestore Rules, deploy, modify Cloud Run env, enable MCP,
or call external providers.

## 2. Goals

Phase 7.10 adds:

- `IGuardedExecutionRuntime`
- `GuardedExecutionRuntime`
- guard request / response models
- guard decision enum
- Release Gate placeholder model
- deny-all / readiness-only release gate evaluator
- offline tests for readiness, blocking, hash mismatch, release gate behavior,
  and no-execution invariants

## 3. Non-goals

Phase 7.10 does not:

- implement a real execution path
- execute real tool actions
- register a real executor
- connect real Firestore
- create Firestore collections
- modify Firestore Rules
- write `users/{userId}/memories`
- write `life_events`
- deploy
- modify Cloud Run env
- enable MCP
- call external APIs
- process real secrets
- push commits

## 4. Added Skeleton

Added files under:

```text
LifeAgent.Api/Services/Agent/GuardedExecution/
```

Files:

- `IGuardedExecutionRuntime.cs`
- `GuardedExecutionRuntime.cs`
- `GuardedExecutionContracts.cs`
- `GuardDecisionType.cs`
- `ReleaseGateContracts.cs`

The runtime consumes `IPendingActionStore`, loads a pending action by id and
user, validates basic readiness checks, evaluates Release Gate placeholder
state, and returns a guard decision.

## 5. Guard Decision Model

Supported decisions include:

- `AllowPreviewOnly`
- `AllowConfirmationOnly`
- `AllowExecutionReady`
- `BlockExecution`
- `RequireReleaseGate`
- `RequireRepreview`
- `RejectStaleAction`
- `RejectPolicyMismatch`
- `RejectRiskLevel`
- `RejectExternalCall`
- `RejectWriteIntent`
- `RejectReplay`
- `RejectCrossUser`
- `RejectExpired`
- `RejectCancelled`
- `RejectMissingConfirmation`
- `RejectToolVersionMismatch`
- `RejectHashMismatch`

`AllowExecutionReady` is future readiness only. It does not execute a tool, does
not write data, and does not call external services.

## 6. Release Gate Placeholder

`DenyAllReleaseGateEvaluator` is the default.

It:

- does not read Cloud Run env
- does not read secrets
- does not read remote configuration
- does not access network
- does not access Firestore
- denies write intent
- denies external calls
- denies high-risk execution
- may allow readiness-only for no-write / no-external low-risk or medium
  preview actions

## 7. State Boundary

The guard runtime may return or record `ExecutionBlocked` or future-gated
`ExecutionReady` through the store skeleton. It never sets `Executed`.

Rules:

- `Confirmed` does not equal `ExecutionReady`.
- `ExecutionReady` does not equal `Executed`.
- client cannot set execution state
- guard runtime evaluates readiness only
- server-only payload is never returned
- reasons are sanitized

## 8. Phase 7.7 / 7.9 Alignment

Phase 7.10 follows Phase 7.7 checks:

- ownership
- status
- TTL
- confirmation reference
- tool id / version
- input hash
- preview hash
- confirmation hash
- write intent
- external call
- risk level
- release gate placeholder decision

It uses the Phase 7.9 `IPendingActionStore` skeleton and does not replace the
existing production Agent confirmation path.

## 9. Verification

Run:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --filter Phase710`
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --filter Phase78`
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --filter Phase79`
- `git diff --stat`
- `git diff --check`
- `git status --short`
- `git log --oneline -4`

Do not run real smoke tests, provider pilots, external API calls, tests
requiring real secrets, tests requiring real Firestore, deploy commands, or
gcloud write commands.

## 10. Future Stage Relationship

Recommended later path:

- Phase 7.11 Store Implementation Design: design concrete storage and migration
  only after explicit approval.
- Phase 7.12 Guard Runtime Integration Plan: plan how guard runtime would be
  wired without execution.
- Phase 8 Release Gate / Online Canary: future planning only. Production
  writes, env changes, Firestore Rules changes, MCP, provider pilots, and
  external calls require explicit approval.

Final conclusion: Phase 7.10 adds a local guard runtime skeleton. It does not
execute actions, write data, call providers, connect Firestore, create
collections, modify Rules, deploy, modify env, enable MCP, or push.

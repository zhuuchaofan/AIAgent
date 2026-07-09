# Phase 7.7 Guarded Execution Design

Date: 2026-07-09

## 1. Background

Phase 7.3 defined the Runtime Trace / Audit Contract. Phase 7.4 defined the
Preview Tool Adapter contract. Phase 7.5 defined the Confirmation Runtime
Contract. Phase 7.6 defined the Pending Action Store logical model, lifecycle,
TTL, idempotency, sensitive payload boundary, query needs, and audit references.

Phase 7.7 defines the future guarded execution boundary. Guarded execution is
the server-side decision layer that determines whether a confirmed pending
action is allowed to become `execution_ready`, and in a later Release Gate
approved phase, whether it can be attempted.

This phase is design-only. It does not implement execution runtime, execute
tools, create Firestore collections, modify Firestore Rules, connect durable
memory, write `life_events`, deploy, modify Cloud Run env, enable MCP, or call
external providers.

## 2. Goals

Phase 7.7 defines:

- guarded execution logical contract
- checks required before a confirmed action may become `execution_ready`
- boundary from `confirmed` to `execution_ready`
- future boundary from `execution_ready` to `executed`
- Release Gate participation in execution decisions
- relationship between policy decision, guard decision, and risk decision
- handling of blocked, rejected, expired, and cancelled actions
- trace and audit events for execution readiness and future attempts
- future real execution entry point, without enabling it
- no-write / default-off / Release Gate principles

The goal is to make future execution fail closed by design. No action should
become executable merely because it was previewed or confirmed.

## 3. Non-goals

Phase 7.7 does not:

- implement real execution runtime
- execute real tool actions
- enable durable memory write
- connect real Firestore Memory runtime
- create Firestore collections
- modify Firestore Rules
- write `users/{userId}/memories`
- write `life_events`
- deploy
- modify Cloud Run environment variables
- enable MCP
- call real external provider APIs
- process or print real secrets
- run provider pilots
- generate real provider reports
- push commits

If future execution work requires real writes, external calls, secrets,
deployment, Cloud Run environment changes, Firestore Rules changes, MCP,
provider pilots, or production data mutation, it must stop for explicit user
approval.

## 4. Guarded Execution Contract

A future guarded execution layer should be a server-side boundary with this
conceptual interface:

```text
GuardedExecution.evaluate(request, context) -> GuardedExecutionResponse
```

### Execution request

Server-provided fields:

- `executionRequestId`
- `pendingActionId`
- `confirmationId`
- `previewId`
- `toolId`
- `toolVersion`
- `adapterId`
- `userSubjectRef`
- `userIdHash`
- `sessionSubjectRef`
- `sessionIdHash`
- `riskLevel`
- `writeIntent`
- `externalCall`
- `releaseGateDecision`
- `policyDecision`
- `guardDecision`
- `validationResult`
- `redactionResult`
- `inputHash`
- `previewHash`
- `confirmationHash`
- `idempotencyKeyHash`
- `traceContext`
- `auditMetadata`
- `schemaVersion`

The client must not provide execution authority, executable args, release gate
state, feature flag state, risk level, input hash, preview hash, confirmation
hash, or guard result.

### Execution response

Recommended fields:

- `success`
- `status`
- `pendingActionId`
- `confirmationId`
- `previewId`
- `toolId`
- `toolVersion`
- `riskLevel`
- `guardDecision`
- `policyDecision`
- `releaseGateDecision`
- `executionReady`
- `executed=false`
- `wroteData=false`
- `externalCallMade=false`
- `blockedReason`
- `traceId`
- `auditEventRef`
- `futureExecutionResultRef`, future field only
- `schemaVersion`

Current phase response rules:

- `executionReady` is a future-gated readiness state only.
- `executed` must be false.
- `wroteData` must be false.
- `externalCallMade` must be false.
- Future execution result refs must not point to real execution output in this
  phase.

### Field classification

Client-visible:

- safe status
- safe blocked reason
- pending action id
- confirmation id
- trace support reference
- `executionReady=false` or safe future-ready status when allowed
- `executed=false`
- `wroteData=false`

Server-only:

- tool id and version
- adapter id
- user and session subject refs
- input / preview / confirmation hashes
- idempotency key hash
- policy snapshot refs
- release gate refs
- server-only payload refs
- execution readiness validation details

Audit-only:

- audit event refs
- internal policy reason refs
- guard decision refs
- release gate decision refs
- replay attempt refs
- cross-user / cross-session failure refs
- blocked execution attempt details

Prohibited:

- raw secrets
- raw prompts
- hidden system prompts
- raw auth claims
- full sensitive memory, document, email, or knowledge context
- complete provider request
- complete executable payload
- complete Firestore document body
- raw idempotency keys

Future-gated:

- `futureExecutionResultRef`
- durable resource refs
- external side-effect result refs
- rollback / compensation refs
- execution attempt payload refs

## 5. Execution Readiness Checks

Before a confirmed action can become `execution_ready`, guard runtime must
validate:

- pending action exists
- pending action belongs to the authenticated user
- session matches, or cross-session execution is explicitly allowed
- pending action is not expired
- confirmation exists and is valid
- confirmation belongs to the same pending action
- confirmation has not been revoked
- action status allows readiness checking
- tool id matches the pending action
- tool version matches the pending action and compatible registry entry
- adapter is still available
- input hash matches the stored validated input
- preview hash matches the stored sanitized preview
- confirmation hash matches the accepted confirmation request
- policy snapshot is still acceptable
- risk level allows execution under current policy
- Release Gate explicitly allows this action type
- write intent is allowed by current gates
- external call is allowed by current gates
- target resource is still available, if referenced
- idempotency and replay checks pass
- previous execution attempt does not already exist, unless replay-safe
- redaction result is still valid
- server-only payload is present and valid when future execution needs it

Failure rules:

- Any failed check must stop execution readiness.
- Failed checks transition to `execution_blocked`, `execution_rejected`, or
  `execution_expired` as appropriate.
- A blocked response may include a sanitized user-visible reason.
- Audit may include structured reason references.
- Audit must not include secrets, raw prompts, full context, or executable
  payloads.

## 6. Guard Decision Model

Recommended guard decisions:

- `allow_preview_only`
- `allow_confirmation_only`
- `allow_execution_ready`
- `block_execution`
- `require_release_gate`
- `require_repreview`
- `reject_stale_action`
- `reject_policy_mismatch`
- `reject_risk_level`
- `reject_external_call`
- `reject_write_intent`
- `reject_replay`
- `reject_cross_user`
- `reject_expired`

Rules:

- Phase 7.7 may design up to `allow_execution_ready`, but it must not execute.
- `executed` remains a future Release Gate state.
- Guard decisions must be auditable.
- Guard decisions must be based on server-side state, not client declarations.
- High-risk actions default to `block_execution` unless a future Release Gate
  explicitly allows them.
- External side-effect actions default to `reject_external_call` unless a
  dedicated boundary and Release Gate approve them.
- Write-intent actions default to `reject_write_intent` when write flags or
  Release Gates are closed.
- Stale hashes, stale policies, expired actions, and replay attempts must fail
  closed.

Relationship between decisions:

- Policy decision answers whether the action is allowed by product / safety
  policy.
- Risk decision answers whether the action's risk level is acceptable in the
  current environment.
- Release Gate decision answers whether production-impacting execution has been
  explicitly approved.
- Guard decision combines those decisions with ownership, TTL, hash,
  idempotency, payload, and target-resource checks.

## 7. Execution State Machine

Execution-related states:

- `preview_created`
- `confirmation_required`
- `confirmation_submitted`
- `confirmed`
- `execution_readiness_checking`
- `execution_ready`, future-gated
- `execution_blocked`
- `execution_rejected`
- `execution_expired`
- `execution_cancelled`
- `execution_attempted`, future-gated
- `executed`, future state
- `execution_failed`, future state
- `execution_rolled_back`, future concept only

State rules:

- `confirmed` does not equal `execution_ready`.
- `execution_ready` does not equal `executed`.
- The client cannot directly set execution state.
- Server-side guard runtime owns all execution state transitions.
- Every execution state transition must emit audit.
- `execution_ready` may be designed but not enabled in Phase 7.7.
- `execution_attempted`, `executed`, `execution_failed`, and
  `execution_rolled_back` are future concepts.
- Future rollback is a concept only; Phase 7.7 does not design concrete
  compensation implementation.

Recommended transitions:

- `confirmed` -> `execution_readiness_checking`
- `execution_readiness_checking` -> `execution_ready`, future-gated
- `execution_readiness_checking` -> `execution_blocked`
- `execution_readiness_checking` -> `execution_rejected`
- `execution_readiness_checking` -> `execution_expired`
- `execution_readiness_checking` -> `execution_cancelled`
- `execution_ready` -> `execution_attempted`, future-gated
- `execution_attempted` -> `executed`, future state
- `execution_attempted` -> `execution_failed`, future state
- `execution_failed` -> `execution_rolled_back`, future concept only

## 8. Release Gate Design

A Release Gate is an explicit approval artifact for production-impacting
execution. It is not a generic feature flag and cannot be supplied by the
client.

Recommended Release Gate fields:

- `releaseGateId`
- `releaseGateVersion`
- `name`
- `allowedActionTypes`
- `allowedToolIds`
- `allowedToolVersions`
- `allowedRiskLevels`
- `allowedWriteIntents`
- `allowedExternalCalls`
- `allowedEnvironments`
- `allowedUserScopes`
- `effectiveFrom`
- `expiresAt`
- `approverRef`
- `approvalReference`
- `policySnapshotRef`
- `auditRef`
- `rollbackPlanRef`
- `cleanupPlanRef`

Rules:

- No Release Gate defaults to allowing real execution.
- Release Gate must be explicitly enabled and scoped.
- Release Gate cannot be provided by the client.
- Release Gate cannot be opened by ordinary broad config accidentally.
- Release Gate must bind action type, tool id, version, risk, environment, user
  scope, and time window.
- Cloud Run env changes require separate approval.
- Firestore Rules changes require separate approval.
- MCP enablement requires separate approval.
- Production writes require separate approval.
- Phase 7.7 designs Release Gate semantics; it does not enable a Release Gate.

## 9. Failure / Block Handling

Failure handling should be structured, auditable, and no-write.

| Scenario | Result | Repreview / reconfirm policy |
| --- | --- | --- |
| Expired action | `execution_expired` | regenerate preview |
| Cancelled action | `execution_cancelled` | create new preview if user restarts |
| Rejected action | `execution_rejected` | create new preview after policy-safe changes |
| Stale preview | `require_repreview` | regenerate preview |
| Stale confirmation | `require_repreview` or `execution_blocked` | reconfirm after new preview |
| Tool version mismatch | `reject_stale_action` | regenerate preview |
| Policy mismatch | `reject_policy_mismatch` | regenerate only if policy allows |
| Risk level too high | `reject_risk_level` | blocked unless Release Gate changes |
| Release Gate missing | `require_release_gate` | wait for explicit approval |
| Write intent blocked | `reject_write_intent` | no write; future gate required |
| External call blocked | `reject_external_call` | no external call; future gate required |
| Replay detected | `reject_replay` | return stored result or block conflict |
| Cross-user attempt | `reject_cross_user` | safe not-found / forbidden behavior |
| Target resource unavailable | `block_execution` | regenerate or cancel |
| Payload missing | `block_execution` | regenerate preview |
| Payload redaction invalid | `block_execution` | regenerate preview after redaction |

Rules:

- User-facing messages must be sanitized.
- Audit should record structured reason refs.
- Sensitive raw content must not be recorded.
- High-risk actions must not auto-retry.
- Failed execution readiness must not auto-downgrade into execution.
- A new preview should create a new pending action rather than mutating an old
  unsafe action into readiness.

## 10. Trace / Audit Integration

Guarded execution should align with Phase 7.3.

Recommended trace / audit events:

- `execution_readiness_check_started`
- `execution_readiness_check_passed`
- `execution_readiness_check_failed`
- `execution_blocked`
- `execution_rejected`
- `execution_ready_marked`, future-gated
- `execution_attempted`, future-gated
- `execution_succeeded`, future state
- `execution_failed`, future state
- `release_gate_evaluated`
- `policy_decision_evaluated`
- `replay_attempt_detected`

Client-visible:

- safe status
- safe blocked reason
- trace support reference
- `executed=false`
- `wroteData=false`

Server-only:

- policy snapshot ref
- release gate ref
- input / preview / confirmation hash
- idempotency hash
- server-only payload ref
- target resource ref
- validation refs

Audit-only:

- audit id
- internal decision refs
- blocked reason refs
- release gate evaluation refs
- replay attempt refs
- cross-user / cross-session failure refs
- future execution attempt refs

Prohibited:

- raw secrets
- raw prompts
- full context
- complete provider request
- complete executable payload
- complete Firestore document body
- raw auth claims
- raw idempotency keys
- raw external provider payloads

Trace and audit should store hashes, references, sanitized summaries, policy
decisions, redaction results, and blocked reason categories.

## 11. Safety Boundary

Guarded Execution is design-only in Phase 7.7.

Rules:

- No real execution occurs in this phase.
- `confirmed` does not mean `executed`.
- `execution_ready` does not mean `executed`.
- No real write occurs in this phase.
- No external API call occurs in this phase.
- No Firestore collection, Rules, or env change occurs in this phase.
- No provider pilot is introduced.
- No real provider report is generated.
- No real Firestore write path is connected.
- No deployment, env, Rules, or MCP change is made.
- Durable memory write remains disabled.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- `life_events` remains unwritten.
- Any real execution requires a separate Release Gate and explicit user
  approval.

Guarded execution should make unsafe actions visible and blocked. It must never
make an unsafe action executable merely because a preview or confirmation
exists.

## 12. Future Stage Relationship

Recommended later path:

- Phase 7.8 Offline Fixture / Mock Tool Regression: create offline fixtures for
  preview, confirmation, pending action, readiness checks, blocked decisions,
  idempotency, and trace / audit snapshots without external API calls.
- Phase 7.9 Store Interface Skeleton: only after user approval, add local
  interfaces or models without creating collections, changing Rules, or
  enabling real execution.
- Phase 7.10 Guard Runtime Skeleton: only after user approval, add a local
  guard evaluator skeleton that returns no-write blocked / readiness decisions
  without write coordinators.
- Phase 8 Release Gate / Online Canary: future planning only. Online canaries,
  production writes, env changes, Firestore Rules changes, provider pilots, MCP,
  and external calls require explicit user approval.

Phase 7.7 stops at guarded execution design. It does not start fixture
regression, store skeletons, guard runtime skeletons, or online canary work.

## 13. Verification Plan

Phase 7.7 verification is docs-only:

- `git diff --stat`
- `git diff --check`
- `git status --short`
- `git log --oneline -4`

Full runtime tests are not required when the diff is documentation-only and no
API, frontend, runtime, schema, deployment, env, Firestore Rules, MCP, external
provider, or production data path changed.

## 14. Closeout Criteria

Phase 7.7 is complete when:

- `docs/phase7_7_guarded_execution_design.md` exists.
- Guarded Execution contract is defined.
- Execution readiness checks are explicit.
- Guard Decision Model is defined.
- Execution state machine is aligned with Phase 7.5 and Phase 7.6.
- Release Gate design is documented.
- Failure and block handling is defined.
- Trace / audit integration is defined.
- Safety boundary is explicit.
- Future stage relationship is documented without entering those stages.
- Verification passes.
- A local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.7 defines how future execution readiness should be
guarded and audited. It does not implement execution runtime, execute tools,
enable writes, connect Firestore Memory runtime, create collections, modify
Rules, write `life_events`, call external providers, deploy, modify env, enable
MCP, or push.

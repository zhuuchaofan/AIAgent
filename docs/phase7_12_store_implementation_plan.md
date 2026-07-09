# Phase 7.12 Store Implementation Plan

Date: 2026-07-09

## 1. Phase 7.12 Goal

Phase 7.12 is a docs-only implementation plan for a future durable Pending
Action Store. It does not implement the store.

The plan aligns:

- Phase 7.6 pending action store model
- Phase 7.9 `IPendingActionStore` skeleton
- Phase 7.10 guarded execution runtime
- Phase 7.11 offline guard chain harness
- existing preview-only agent pending action behavior

The future candidate implementation may use Firestore, but this phase does not
enable it. The design covers collection shape, document schema, TTL, indexes,
Rules principles, migration, rollback, DI wiring, feature flags, Release Gate,
observability, and tests.

## 2. Non-goals

Phase 7.12 does not:

- implement a real Firestore store
- create Firestore collections
- modify Firestore Rules
- modify Cloud Run environment variables
- connect production DI
- enable durable memory write
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- call external provider APIs
- generate real provider reports
- process or print real secrets
- deploy
- push commits

If any future step requires real Firestore writes, external calls, secrets,
deployment, production configuration changes, Rules changes, MCP, or a real
execution path, work must stop for explicit approval.

## 3. Candidate Firestore Collection Design

Two candidate shapes are available for a future Firestore implementation.

### Option A: user-scoped collection

```text
users/{userId}/pendingActions/{pendingActionId}
```

Pros:

- matches existing user-scoped data layout
- keeps tenant ownership visible in the path
- simplifies Rules ownership checks
- reduces accidental cross-user queries
- fits the Phase 7.6 model that client access is user-scoped but server-owned

Cons:

- cross-user operational cleanup needs collection group queries
- global audit/debug queries require careful server-only indexing
- document path includes user id, so internal references must still avoid
  leaking raw auth details outside server logs

### Option B: global collection

```text
pendingActions/{pendingActionId}
```

Pros:

- simpler global cleanup and debugging queries
- easier unique idempotency scanning across all users
- one collection for operational tooling

Cons:

- ownership is only document-field based
- Rules and server code must be stricter to avoid cross-user reads
- client access is riskier and should likely be fully denied
- harder to align with existing `users/{userId}/` isolation

### Recommendation

Recommended future implementation candidate:

```text
users/{userId}/pendingActions/{pendingActionId}
```

This is only a future candidate, not a current implementation. It keeps the
ownership boundary close to existing LifeOS data paths while allowing server
APIs to return only sanitized projections.

Document ids should be server-generated, non-guessable ids such as
`pa_{ulid-or-guid}`. The store should persist a stable `userSubjectRef` or
`userIdHash`, a `sessionSubjectRef` or `sessionIdHash`, optional
`tenantScope`, `environmentScope`, and `schemaVersion`. Schema changes should
be additive first, versioned, and migrated by explicit future jobs only after
approval.

## 4. Firestore Document Schema Draft

Draft document fields:

- `pendingActionId`
- `previewId`
- `confirmationId`
- `toolId`
- `toolVersion`
- `adapterId`
- `actionType`
- `userSubjectRef` or `userIdHash`
- `sessionSubjectRef` or `sessionIdHash`
- `tenantScope`
- `environmentScope`
- `riskLevel`
- `status`
- `createdAt`
- `updatedAt`
- `expiresAt`
- `idempotencyKeyHash`
- `inputHash`
- `previewHash`
- `confirmationHash`
- `policySnapshotRef`
- `traceId`
- `auditEventRefs`
- `sanitizedPreviewRef`
- `serverOnlyPayloadRef`
- `redactionMetadata`
- `validationSnapshot`
- `blockedReason`
- `cancellationReason`
- `guardDecisionRef`
- `releaseGateDecisionRef`
- `futureExecutionHandoffRef`
- `schemaVersion`

Client-visible projection:

- `pendingActionId`
- `previewId`
- safe action title / summary resolved from `sanitizedPreviewRef`
- `actionType`
- `status`
- safe `riskLevel`
- `createdAt`
- `expiresAt`
- sanitized `blockedReason`
- support-safe `traceId` or trace reference
- `executed=false`
- `wroteData=false`

Server-only:

- `toolId`
- `toolVersion`
- `adapterId`
- `userSubjectRef` / `userIdHash`
- `sessionSubjectRef` / `sessionIdHash`
- `tenantScope`
- `environmentScope`
- `idempotencyKeyHash`
- `inputHash`
- `previewHash`
- `confirmationHash`
- `policySnapshotRef`
- `serverOnlyPayloadRef`
- `redactionMetadata`
- `validationSnapshot`
- status transition guard data

Audit-only:

- `auditEventRefs`
- `guardDecisionRef`
- `releaseGateDecisionRef`
- policy decision refs
- validation refs
- replay / cross-user / stale action refs
- cleanup refs

Future-gated:

- `futureExecutionHandoffRef`
- future execution attempt refs
- future resource refs
- rollback / compensation refs

Prohibited:

- raw secrets
- raw prompts
- hidden system prompts
- full context
- complete provider requests
- complete executable payloads
- complete Firestore document bodies
- real tokens or API keys
- raw auth claims
- raw idempotency keys

## 5. Server-only Payload Strategy

The pending action document should store a payload reference, not a payload
body. A future payload store should be separate from the main document and
should be inaccessible to clients.

Recommended future strategy:

- store `serverOnlyPayloadRef` in the pending action document
- store payload body separately, possibly in a secure collection or object
  reference
- use envelope encryption or equivalent field-level protection if payload body
  is persisted
- use short TTL for payloads, especially high-risk actions
- keep sanitized preview separate from server-only payload
- never include payload bodies in trace or audit events
- record only hashes, refs, redaction metadata, and sanitized reasons
- if payload is missing, guard runtime must block execution
- if payload is expired, mark the pending action expired or execution-blocked
- separate payload retention from audit retention

Phase 7.12 does not implement a payload store.

## 6. TTL / Expiration / Cleanup Plan

Default TTL candidate:

- low-risk read-only or preview actions: 15 minutes
- write-intent actions: 5 to 10 minutes
- external-call or critical release-gated actions: 2 to 5 minutes

The store should persist `expiresAt` on every record. Runtime reads should use
lazy expiration first: if `expiresAt <= now`, the store or guard runtime should
return / record `expired` and block further confirmation or readiness.

Future cleanup options:

- scheduled cleanup job, future only
- Firestore TTL policy on `expiresAt`, future only
- server-only payload cleanup before or at pending action expiry
- audit refs retained according to audit retention policy

Expired actions should not become `confirmed`, `execution_ready`, or
`executed`. Cleanup jobs are operational write paths and require Release Gate
approval before production use.

## 7. Index Design Draft

Runtime required:

- user + status + expiresAt
- pendingActionId
- previewId
- confirmationId
- idempotencyKeyHash
- status + expiresAt

Audit / debug:

- traceId
- toolId + toolVersion
- createdAt
- updatedAt
- riskLevel + status

Cleanup:

- status + expiresAt
- expiresAt
- updatedAt for stale blocked records

Composite index candidates:

- `userSubjectRef`, `status`, `expiresAt`
- `userSubjectRef`, `idempotencyKeyHash`
- `userSubjectRef`, `previewId`
- `userSubjectRef`, `confirmationId`
- `status`, `expiresAt`
- `toolId`, `toolVersion`, `createdAt`

Client-visible queries should not expose full pending action documents. Clients
should call server APIs that return sanitized projections only.

## 8. Firestore Rules Draft

Rules design principles only; this phase does not modify `firestore.rules`.

Rules should enforce:

- client cannot directly read full pending action documents
- client cannot directly create pending action documents
- client cannot directly update status
- client cannot read `serverOnlyPayloadRef` targets
- client cannot read audit-only refs
- user ownership is checked server-side
- cross-user access returns not found / blocked
- server-only APIs own status transitions
- future Rules changes require a separate Release Gate

Possible policy shape:

```text
pendingActions full documents: deny client read/write
pendingActions sanitized projections: server API only
server-only payload collection: deny all client access
audit refs: deny all client access
status transitions: server-only
```

This is a draft policy, not an applied Rules change.

## 9. DI / Wiring Plan

`IPendingActionStore` should remain the runtime tooling interface. Fake and
in-memory stores should remain test-only.

Future implementation candidate names:

- `FirestorePendingActionStore`
- `DisabledPendingActionStore`
- `NoopPendingActionStore`

Production DI should be default-off. A feature flag candidate:

```text
AgentRuntime__PendingActionStore__Mode=disabled|emulator|firestore
```

Additional future flags may include:

- `AgentRuntime__GuardedExecution__Enabled=false`
- `AgentRuntime__PendingActionStore__AllowFirestore=false`
- `AgentRuntime__PendingActionStore__PreviewOnly=true`

Cloud Run env changes are future-only and require approval. Guard runtime and
confirmation runtime should consume the interface, not Firestore classes
directly. Rollback should switch DI to disabled / preview-only mode and block
new durable pending action writes.

Existing `IPendingAgentActionStore` / `FirestorePendingAgentActionStore` is the
current preview action path and should not be silently replaced by the Phase
7.9 `IPendingActionStore` contract.

## 10. Migration / Rollback Plan

Future migration steps:

1. approve collection, schema, Rules, index, env, and rollback plan
2. add emulator-only implementation tests
3. add disabled-mode production DI
4. add Firestore implementation behind default-off flag
5. run offline tests and emulator tests
6. deploy default-off only after approval
7. run preview-only canary only after Release Gate approval

Schema strategy:

- persist `schemaVersion`
- prefer additive changes
- reject unknown required fields
- support migration readers before writers

Dual-write should be forbidden unless separately approved. Backfill is likely
not required for short-lived pending actions; existing actions can expire under
the old path. Rollback should disable the feature flag, stop creating new
durable pending actions, let existing actions expire, clean payload refs only
through approved cleanup, and revert Rules / indexes / env through documented
operations.

Rollback validation:

- no new pending action writes
- no execution path enabled
- expired actions cannot confirm
- server-only payload refs are inaccessible
- offline and emulator tests pass
- logs contain no raw secrets, prompts, full context, or payload bodies

## 11. Observability / Trace / Audit Plan

Future store events:

- `pending_action_store_create_attempted`
- `pending_action_store_created`
- `pending_action_store_get`
- `pending_action_store_update_status_attempted`
- `pending_action_store_update_status_succeeded`
- `pending_action_store_update_status_blocked`
- `pending_action_store_expired`
- `pending_action_store_cleanup_attempted`
- `pending_action_store_cleanup_completed`
- `pending_action_store_payload_missing`
- `pending_action_store_rules_denied`

Trace and audit must record only:

- ids
- hashes
- references
- sanitized status / reason
- policy decision refs
- guard decision refs
- release gate decision refs
- redaction summaries

Trace and audit must not record:

- raw secrets
- raw prompts
- full context
- complete executable payload
- complete provider request
- complete Firestore document body
- real tokens / API keys
- raw idempotency keys

## 12. Testing Plan

Existing tests:

- Phase 7.8 fixture tests
- Phase 7.9 store skeleton tests
- Phase 7.10 guard runtime tests
- Phase 7.11 offline chain tests

Future layers:

- fake store contract tests
- disabled-mode DI tests
- Firestore emulator store tests
- Firestore Rules emulator tests
- TTL / lazy expiration tests
- cleanup job tests, future only
- guard integration tests against emulator
- sanitized projection tests
- rollback tests
- canary smoke, Phase 8 / Release Gate only

Real Firestore should not be used for early tests. Firestore emulator is the
preferred next test boundary, but introducing it should be a separate phase.
Online canary belongs to Phase 8 / Release Gate and requires explicit approval.

## 13. Release Gate Checklist

Before implementing or enabling a real Firestore store:

- user explicitly approves the phase
- collection design approved
- document schema approved
- Rules draft approved
- index plan approved
- Cloud Run env changes approved
- emulator tests passed
- offline tests passed
- rollback plan approved
- observability fields approved
- no raw secret / no raw prompt / no full context verified
- production default-off verified
- server-only payload strategy approved
- canary plan approved

## 14. Follow-up Phase Suggestions

Recommended next phases:

- Phase 7.13 Firestore Emulator / Rules Test Plan, docs-only or test-only
- Phase 7.14 Store Implementation Skeleton with Emulator, requires approval
- Phase 7.15 Production Wiring Plan, docs-only
- Phase 8 Release Gate / Online Canary, requires approval

Final conclusion: Phase 7.12 is a docs-only plan for a future durable Pending
Action Store. It does not implement Firestore, create collections, modify Rules,
modify env, deploy, connect production DI, write data, call providers, enable
MCP, register real executors, or push.

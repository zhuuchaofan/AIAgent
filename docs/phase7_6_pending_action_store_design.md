# Phase 7.6 Pending Action Store Design

Date: 2026-07-09

## 1. Background

Phase 7.3 defined the Runtime Trace / Audit Contract. Phase 7.4 defined the
Preview Tool Adapter contract and the boundary between sanitized preview and
server-only future execution handoff. Phase 7.5 defined the Confirmation
Runtime Contract and made explicit that `confirmed` does not mean `executed`.

Existing Agent Preview work already has a pending action concept and an
implementation path that stores current preview-only pending actions under:

```text
users/{userId}/agent_pending_actions/{actionId}
```

Phase 7.6 does not change that path, create collections, modify Firestore
Rules, or implement a new store. It defines the logical storage contract needed
by future preview, confirmation, trace, audit, idempotency, cleanup, and guarded
execution phases.

Phase 6 Memory Engine remains preview-only / default-off. Durable memory write
is disabled, the real Firestore Memory runtime is not connected, and
`users/{userId}/memories` is not written.

## 2. Goals

Phase 7.6 defines:

- pending action store logical data model
- server-side storage boundary for pending actions
- references between pending actions, previews, confirmations, trace, and audit
- lifecycle and TTL semantics
- idempotency and replay protection fields
- status transition persistence rules
- logical query patterns, index recommendations, and cleanup strategy
- future execution handoff storage boundary
- no-write / default-off / Release Gate rules

The goal is a storage contract, not a storage implementation.

## 3. Non-goals

Phase 7.6 does not:

- implement a real pending action store
- create Firestore collections
- modify Firestore Rules
- connect real Firestore Memory runtime
- enable durable memory write
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- call write coordinators
- deploy
- modify Cloud Run environment variables
- enable MCP
- call real external provider APIs
- process or print real secrets
- run provider pilots
- generate real provider reports
- push commits

If future store work requires Firestore collection changes, Rules changes,
environment changes, real writes, external API calls, secrets, MCP, deployment,
or production data mutation, it must stop for explicit user approval.

## 4. Pending Action Store Logical Model

A pending action record should be a server-owned record representing a proposed
action that may be confirmed, cancelled, rejected, expired, blocked, or in a
future gated phase executed.

Recommended logical fields:

- `pendingActionId`
- `previewId`
- `confirmationId`, nullable
- `toolId`
- `toolVersion`
- `adapterId`
- `actionType`
- `userSubjectRef`
- `userIdHash`
- `sessionSubjectRef`
- `sessionIdHash`
- `tenantScope`
- `environmentScope`
- `riskLevel`
- `status`
- `createdAt`
- `updatedAt`
- `expiresAt`
- `confirmedAt`
- `cancelledAt`
- `rejectedAt`
- `expiredAt`
- `blockedAt`
- `executionReadyAt`, future-gated
- `executedAt`, future state
- `idempotencyKeyHash`
- `inputHash`
- `previewHash`
- `confirmationRequestHash`
- `policySnapshotRef`
- `traceId`
- `auditEventRefs`
- `sanitizedPreviewRef`
- `serverOnlyActionPayloadRef`
- `redactionMetadata`
- `validationSnapshot`
- `blockedReason`
- `cancellationReason`
- `futureExecutionHandoffRef`, future-gated
- `schemaVersion`

### Field classification

Client-visible:

- `pendingActionId`
- `previewId`
- safe action title and summary from `sanitizedPreviewRef`
- `actionType`
- `status`
- `riskLevel`, when safe
- `createdAt`
- `expiresAt`
- safe `blockedReason`
- trace support reference

Server-only:

- `toolId`
- `toolVersion`
- `adapterId`
- `userSubjectRef`
- `sessionSubjectRef`
- `tenantScope`
- `environmentScope`
- `idempotencyKeyHash`
- `inputHash`
- `previewHash`
- `confirmationRequestHash`
- `policySnapshotRef`
- `serverOnlyActionPayloadRef`
- `redactionMetadata`
- `validationSnapshot`
- internal blocked reason refs
- status transition guard data

Audit-only:

- `auditEventRefs`
- internal policy decision refs
- internal validation refs
- redaction result refs
- replay attempt refs
- cross-user / cross-session failure refs
- retention class refs

Prohibited:

- raw tokens
- raw credentials
- API keys
- cookies
- raw auth claims
- prompt secrets
- hidden system prompts
- raw provider payloads
- full sensitive memory, document, email, or knowledge context by default
- raw replayable idempotency keys
- unredacted cross-user data

Future-gated:

- `futureExecutionHandoffRef`
- `executionReadyAt`
- `executedAt`
- execution result refs
- created / updated / deleted resource refs
- compensation refs

Future-gated fields may exist in the logical schema, but Phase 7.6 does not
enable them.

## 5. Sensitive Payload Boundary

The pending action store must separate sanitized preview from executable
payload.

Rules:

- The client must not hold complete action args.
- The client must not hold secrets, tokens, raw prompts, hidden system prompts,
  full knowledge context, raw customer data, or server-only execution details.
- The sanitized preview is for user review.
- The server-only payload is for future validation and possible execution
  handoff only after confirmation and Release Gate approval.
- The executable payload is a future concept in Phase 7.6 and must not be used
  to execute a real action.

Recommended storage strategy:

- Store only the minimum server-only payload required to revalidate the action.
- Prefer storing `serverOnlyActionPayloadRef` instead of embedding large payloads
  directly in the pending action record.
- Use envelope encryption or equivalent field-level protection for sensitive
  server-only payloads if future implementation stores them.
- Store `inputHash` and `previewHash` for integrity checks.
- Store `redactionMetadata` describing what was removed or summarized.
- Store `sanitizedPreviewRef` separately from `serverOnlyActionPayloadRef`.
- Store source refs and snippets only when needed, never full sensitive context
  by default.

Trace and audit must never persist raw server-only payloads. They should record
hashes, references, redaction results, policy decisions, and sanitized
summaries.

If a preview cannot be safely separated from executable payload, pending action
creation must fail closed.

## 6. State Persistence Rules

The store should persist the Phase 7.5 confirmation state machine.

Persistable states:

- `preview_created`
- `confirmation_required`
- `confirmation_submitted`
- `confirmed`
- `cancelled`
- `rejected`
- `expired`
- `confirmation_blocked`
- `execution_blocked`
- `execution_ready`, future-gated
- `executed`, future state

Rules:

- Only the server can write status.
- The client cannot directly set `status`.
- `confirmed` does not equal `executed`.
- `execution_ready` and `executed` require future Release Gate approval.
- Every status change must produce an audit event reference.
- Every status change must update `updatedAt`.
- Every status change must validate ownership, TTL, status version, tool
  version, input hash, preview hash, and risk policy as applicable.
- Blocked transitions should preserve a sanitized `blockedReason` and an
  audit-only detailed reason ref.
- Cancelled, rejected, expired, and blocked actions must not become executable.

Repeated confirm rules:

- Repeating confirm for the same already-confirmed action should return the same
  stored confirmation result with `idempotent=true`.
- Repeating cancel for the same already-cancelled action should return the same
  stored cancellation result with `idempotent=true`.
- Confirm after cancel must fail closed.
- Confirm after reject must fail closed unless a future regenerate-preview
  contract creates a new pending action.
- Confirm after expiration must fail as `expired`.
- Reconfirming an `execution_blocked` action should return the stored blocked
  result unless a future policy explicitly allows regeneration.

Expiration rules:

- Expiration may be set lazily during read / confirm.
- Once a record is marked `expired`, execution must remain impossible.
- Expired records may retain sanitized preview and audit refs while the
  server-only action payload becomes unavailable or deleted.

## 7. TTL / Expiration / Cleanup Design

Recommended TTL defaults:

- Low-risk preview action: 30 minutes.
- Medium-risk preview action: 10 minutes.
- High-risk internal write proposal: 5 minutes.
- External side-effect proposal: 2 to 5 minutes, depending on capability.
- Critical or destructive action: shortest practical TTL and typed confirmation
  in a future UX contract.

Expiration calculation:

- `expiresAt` is generated server-side.
- A pending action is expired when `now >= expiresAt`.
- The runtime should treat expired records as expired even if the stored status
  has not yet been lazily updated.

Cleanup options:

- Lazy expiration during read / confirm.
- Query-time filtering to show only active pending actions.
- Future scheduled cleanup job for expired and terminal records.
- Future payload cleanup job that removes server-only payload refs before audit
  retention expires.

Retention split:

- Audit references should be retained according to audit retention policy.
- Server-only action payload should have a shorter retention window.
- Sanitized preview may be retained longer if it contains no sensitive raw
  context.
- Expired or terminal records should keep enough metadata to explain user-facing
  status and support audit without retaining executable payload.

Cleanup jobs are future work. Phase 7.6 only defines the contract.

## 8. Idempotency / Replay Protection

The store should support replay-safe confirmation and future execution.

Recommended fields:

- `idempotencyKeyHash`
- `inputHash`
- `previewHash`
- `confirmationRequestHash`
- `userIdHash`
- `sessionIdHash`
- `toolId`
- `toolVersion`
- `policySnapshotRef`
- `createdAt`
- `expiresAt`

Binding rules:

- Idempotency is scoped to user, pending action, tool id, tool version, and
  confirmation intent.
- A replayed matching confirm returns the stored result.
- A replayed matching cancel returns the stored result.
- A mismatched idempotency key or confirmation request hash returns
  `idempotency_conflict`.
- Cross-user replay must fail closed without revealing the action exists.
- Cross-session replay must fail when session binding is required.
- Stale tool version must block confirmation or future execution.
- Stale policy snapshot must block confirmation or require a new preview.
- Expired confirm must not execute and must mark or return `expired`.

Replay attempts must emit audit events with safe refs, hashes, and categories.
They must not include raw request payload, raw prompt, or full context.

## 9. Query / Index Design

This section defines logical queries only. Firestore is a future candidate, not
a Phase 7.6 implementation requirement.

Runtime-required queries:

- by `pendingActionId` within authenticated user scope
- by `previewId` within authenticated user scope
- active pending actions by user and status
- active pending actions by user and `expiresAt`
- by `idempotencyKeyHash` within user / pending action scope

Confirmation-required queries:

- pending action by id and user
- pending action by id, status, and expiry
- pending action by id and tool version
- pending action by id and input / preview hash

Audit / debug queries:

- by `traceId`
- by audit event reference
- by `confirmationId`
- by tool id / version
- by status and time range
- by blocked reason category
- by replay / idempotency conflict category

Client exposure:

- The client may list its own active pending actions only through sanitized
  response shapes.
- The client must not query by audit refs, hashes, policy refs, server-only
  payload refs, or cross-user fields.
- The client must not receive debug-only indexes or internal reasons.

Future Firestore notes:

- Collection naming, document id format, composite indexes, TTL indexes, Rules,
  migrations, and backfills require a separate reviewed phase.
- Any Firestore collection or Rules change requires explicit approval.
- No Phase 7.6 document should be read as permission to create or modify a
  Firestore collection.

## 10. Trace / Audit Integration

The store should reference trace and audit events rather than embedding raw
trace or audit payloads.

Recommended event references:

- `pending_action_created`
- `pending_action_updated`
- `confirmation_submitted`
- `confirmation_accepted`
- `confirmation_rejected`
- `confirmation_blocked`
- `pending_action_expired`
- `pending_action_cancelled`
- `execution_blocked`
- `future_execution_ready`, future-gated
- `future_executed`, future state

Store fields should contain:

- `traceId`
- `correlationId`
- `auditEventRefs`
- `lastAuditEventRef`
- `statusTransitionAuditRefs`
- `redactionResultRef`
- `policyDecisionRef`
- `validationSnapshotRef`
- `idempotencyDecisionRef`

Visibility classification:

Client-visible:

- trace support reference
- safe status
- safe blocked reason
- safe preview summary

Server-only:

- trace id
- correlation id
- policy refs
- validation refs
- redaction refs
- idempotency refs
- server-only payload refs

Audit-only:

- audit event refs
- internal decision refs
- replay attempt refs
- cross-user / cross-session blocked refs
- retention class

Prohibited:

- raw secrets
- raw prompts
- full context
- complete provider request
- complete executable payload
- raw auth claims
- raw idempotency keys
- raw external provider payloads

Trace and audit should use hashes, references, sanitized summaries, policy
decisions, and redaction results.

## 11. Safety Boundary

Pending Action Store saves proposed actions. It does not execute actions.

Rules:

- Store presence does not mean the action is confirmed.
- `confirmed` does not mean `executed`.
- `execution_ready` and `executed` are future Release Gate states.
- Any real write must be separately approved.
- Any Firestore collection, Rules, or env change must be separately approved.
- Any external API call must be explicitly approved.
- Phase 7.6 must not introduce provider pilot runs.
- Phase 7.6 must not generate real provider reports.
- Phase 7.6 must not connect a real Firestore write path.
- Phase 7.6 must not modify deployment, Cloud Run env, Firestore Rules, or MCP.
- Durable memory write remains disabled.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- `life_events` remains unwritten by Phase 7.6 work.

The store may hold design references for future execution, but those references
must not grant execution authority in this phase.

## 12. Future Stage Relationship

Recommended later path:

- Phase 7.7 Guarded Execution Design: define execution entry criteria,
  idempotent write coordinator boundaries, Release Gate checks, compensation,
  rollback, and execution audit. This should remain design-only unless the user
  approves implementation.
- Phase 7.8 Offline Fixture / Mock Tool Regression: build fixture-based tests
  for preview, confirmation, pending action lifecycle, expiration, and replay
  behavior without external API calls or production data.
- Phase 7.9 Store Interface Skeleton: only after user approval, add a local
  interface / model skeleton without creating collections, changing Rules, or
  enabling real execution.
- Phase 8 Release Gate / Online Canary: future planning only. Online canaries,
  production writes, env changes, Firestore Rules changes, provider pilots, and
  external calls require explicit user approval.

Phase 7.6 stops at pending action store design. It does not start guarded
execution, mock regression, store interface implementation, or online canary
work.

## 13. Verification Plan

Phase 7.6 verification is docs-only:

- `git diff --stat`
- `git diff --check`
- `git status --short`
- `git log --oneline -4`

Full runtime tests are not required when the diff is documentation-only and no
API, frontend, runtime, schema, deployment, env, Firestore Rules, MCP, external
provider, or production data path changed.

## 14. Closeout Criteria

Phase 7.6 is complete when:

- `docs/phase7_6_pending_action_store_design.md` exists.
- Logical data model is defined.
- Field classification is explicit.
- Sensitive payload boundary is defined.
- State persistence rules align with Phase 7.5.
- TTL, expiration, cleanup, idempotency, and replay protection are defined.
- Query and index needs are documented without binding implementation.
- Trace / audit references are defined.
- Safety boundary is explicit.
- Future stage relationship is documented without entering those stages.
- Verification passes.
- A local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.6 defines the logical pending action store contract.
It does not implement a store, create collections, modify Firestore Rules,
connect real Firestore Memory runtime, enable durable memory write, write
`life_events`, execute tools, call external providers, deploy, modify env, or
push.

# Phase 7.5 Confirmation Runtime Contract

Date: 2026-07-09

## 1. Background

Phase 7.3 defined the Runtime Trace / Audit Contract. Phase 7.4 defined the
Preview Tool Adapter contract, including sanitized previews, server-side
pending actions, field visibility, redaction rules, and the future execution
handoff boundary.

Phase 7.5 defines the confirmation runtime contract. Confirmation is the point
where a user approves, cancels, rejects, or attempts to confirm a server-side
pending action. It is not the point where a tool automatically executes.

Existing Agent confirmation already supports preview-only confirm / cancel
semantics for pending actions, with production pending actions stored under:

```text
users/{userId}/agent_pending_actions/{actionId}
```

Phase 6 Memory Engine remains preview-only / default-off. Durable memory write
is disabled, the real Firestore Memory runtime is not connected, and
`users/{userId}/memories` is not written. Any future write path remains behind
feature gates and a separate Release Gate.

This document is design-only. It does not implement confirmation runtime code,
execute tools, modify deployment configuration, change Firestore Rules, enable
MCP, or call external providers.

## 2. Goals

Phase 7.5 defines:

- confirmation runtime input and output contracts
- how a confirmation request binds to a server-side pending action
- confirm / cancel / reject / expire / blocked state transitions
- required pre-confirmation revalidation
- the boundary between confirmation and future execution
- trace / audit integration with Phase 7.3
- consumption of Phase 7.4 preview and pending action contracts
- no-write / default-off / Release Gate rules for confirmation

The core rule is: confirmed is not executed. Confirmation records user intent
against a server-side pending action. Execution remains a separate, guarded,
future path.

## 3. Non-goals

Phase 7.5 does not:

- implement runtime code
- execute real tool actions
- enable durable memory write
- connect real Firestore Memory runtime
- write `users/{userId}/memories`
- write `life_events`
- call write coordinators
- deploy
- modify Cloud Run environment variables
- modify Firestore Rules
- enable MCP
- call real external provider APIs
- send local case data, knowledge context, customer data, or project content to
  third-party providers
- process or print real secrets
- enable provider pilot runs
- generate real provider evaluation reports
- push commits

If confirmation work requires real writes, external calls, secrets, deployment,
Cloud Run environment changes, Firestore Rules changes, MCP, or production data
mutation, that work must stop for explicit user approval.

## 4. Confirmation Runtime Contract

A future confirmation runtime should be a server-side boundary with this
conceptual interface:

```text
ConfirmationRuntime.submit(request, context) -> ConfirmationResponse
```

### Confirmation request

Client-provided fields:

- `pendingActionId`
- `previewId`, optional but recommended
- `confirmationIntent`, one of `confirm`, `cancel`, or `reject`
- `confirmationText`, required only for high-risk policies
- `clientTraceRef`, optional support reference
- `idempotencyKey`, optional client request id for retry detection

Client-provided fields are not authority. They identify the pending action and
the user's requested decision only.

Server-resolved fields:

- `requestId`
- `confirmationId`
- `traceId`
- `correlationId`
- `userRef`
- `sessionRef`
- `authContextRef`
- `pendingAction`
- `toolName`
- `toolVersion`
- `adapterId`
- `inputHash`
- `previewHash`
- `policySnapshotRef`
- `riskLevel`
- `featureGateState`
- `releaseGateState`
- `expiresAt`

Server-recomputed fields:

- pending action ownership
- session binding result
- TTL / expiration result
- current status
- tool registry compatibility
- tool id and version compatibility
- input hash
- preview hash
- risk policy decision
- guard policy decision
- redaction policy validity
- external call flag
- write intent flag
- Release Gate decision
- duplicate / replay decision
- idempotency status

The runtime must not trust client-provided `userId`, tool args, execution args,
preview payloads, input hashes, preview hashes, feature gates, risk level,
release gate state, or server-only execution descriptors.

### Confirmation response

Client-visible fields:

- `success`
- `status`
- `message`
- `confirmationId`
- `pendingActionId`
- `previewId`
- `actionType`
- `lifecycleStatus`
- `confirmationStatus`
- `previewOnly`
- `wroteData=false`
- `executionEnabled=false`
- `idempotent`
- `expiresAt`
- `safeBlockedReason`
- `traceId`

Server-only response context:

- validation result details
- policy snapshot reference
- guard decision reference
- input hash
- preview hash
- registry entry reference
- feature flag key
- release gate decision
- idempotency key hash
- future execution handoff token

The `futureExecutionHandoffToken` is design-only in Phase 7.5. It must not be
returned to the client and must not enable execution.

Response rules:

- `previewOnly` must remain `true` in no-write phases.
- `wroteData` must remain `false` in Phase 7.5.
- `executionEnabled` must remain `false` in Phase 7.5.
- `confirmed` means the user approved a pending action reference.
- `confirmed` does not mean a business resource was created, updated, deleted,
  or sent.
- Blocked responses may return sanitized reasons to the client.
- Internal validation details belong in audit and must not expose secrets,
  prompt text, raw context, or cross-user data.

## 5. Field Visibility Contract

### Client-provided

The client may provide:

- pending action id
- preview id
- confirmation intent
- confirmation text
- optional client retry id

The client may not provide authoritative:

- user id
- session authority
- tool args
- execution args
- server-side payload
- risk level
- feature gate state
- release gate state
- input hash
- preview hash
- future execution handoff

### Server-only

The server must read or compute:

- authenticated user reference
- session reference
- pending action record
- action ownership
- TTL
- stored tool id and version
- stored adapter id
- stored schema refs
- input hash
- preview hash
- policy snapshot reference
- guard policy state
- redaction policy state
- idempotency status
- release gate decision

### Client-returnable

The server may return:

- status and lifecycle status
- safe user-visible message
- action type
- pending action id
- preview id
- trace id
- confirmation id
- expiration time
- sanitized blocked reason
- `previewOnly=true`
- `wroteData=false`
- `executionEnabled=false`

### Prohibited in client response

The server must not return:

- raw tool args
- server-only execution descriptor
- future execution handoff token
- auth claims
- tokens
- credentials
- API keys
- prompt secrets
- hidden system prompts
- raw provider payloads
- full sensitive memory, document, email, or knowledge context
- unredacted cross-user references
- raw idempotency keys

### Prohibited in trace / audit raw form

Trace and audit must not store raw:

- secrets or credentials
- prompt secrets
- hidden system prompts
- raw auth claims
- complete sensitive context by default
- raw external provider payloads
- replayable idempotency keys

Use hashes, ids, counts, categories, schema refs, and sanitized summaries.

## 6. Confirmation State Machine

Recommended states:

- `preview_created`: preview adapter generated a sanitized preview.
- `confirmation_required`: pending action is waiting for user decision.
- `confirmation_submitted`: server received a confirm / cancel / reject
  request and started validation.
- `confirmed`: user intent was accepted for the server-side pending action.
- `cancelled`: user cancelled the pending action.
- `rejected`: user or server policy rejected the proposal.
- `expired`: TTL elapsed before a valid terminal decision.
- `confirmation_blocked`: confirmation request failed validation, ownership,
  session, hash, policy, guard, or idempotency checks.
- `execution_blocked`: confirmation was accepted or attempted, but execution is
  blocked by feature gates, Release Gate, policy, or current phase boundary.
- `execution_ready`: future gated state only; not enabled by Phase 7.5.
- `executed`: future state only; not enabled by Phase 7.5.

### Server-only states

Only the server may set:

- `confirmation_submitted`
- `confirmed`
- `cancelled`
- `rejected`
- `expired`
- `confirmation_blocked`
- `execution_blocked`
- `execution_ready`
- `executed`

The client can request `confirm`, `cancel`, or `reject`; it cannot directly set
terminal lifecycle state.

### Allowed transitions

Recommended transitions:

- `preview_created` -> `confirmation_required`
- `confirmation_required` -> `confirmation_submitted`
- `confirmation_submitted` -> `confirmed`
- `confirmation_submitted` -> `cancelled`
- `confirmation_submitted` -> `rejected`
- `confirmation_submitted` -> `expired`
- `confirmation_submitted` -> `confirmation_blocked`
- `confirmed` -> `execution_blocked` in no-write phases
- `confirmed` -> `execution_ready` only in a future Release Gate approved path
- `execution_ready` -> `executed` only in a future execution phase

Phase 7.5 may design all states, but it must not enable `execution_ready` or
`executed`.

### Idempotency and repeated requests

Rules:

- Repeated confirm for an already confirmed pending action should return the
  same confirmed preview result with `idempotent=true`.
- Repeated cancel for an already cancelled pending action should return the
  same cancelled result with `idempotent=true`.
- Confirm after cancelled must fail closed.
- Cancel after confirmed must fail closed unless a future explicit undo
  contract exists.
- Confirm after expired must return expired.
- Cross-user confirm must return not found or forbidden-safe behavior without
  revealing the action exists.
- Cross-session confirm must fail when the pending action requires session
  binding and the session does not match.
- Tool version mismatch must enter `confirmation_blocked`.
- Input hash mismatch must enter `confirmation_blocked`.
- Preview hash mismatch must enter `confirmation_blocked`.
- Idempotency conflict must enter `confirmation_blocked` and must not execute.

## 7. Pre-confirmation Validation

Before accepting confirmation, the runtime must revalidate:

- pending action exists
- pending action belongs to the authenticated user
- session matches, or cross-session confirmation is explicitly allowed
- TTL has not expired
- pending action status allows the requested decision
- preview hash matches the stored server-side preview
- input hash matches the stored validated input
- tool id matches the stored pending action
- tool version matches the stored pending action or compatible registry entry
- adapter id matches the stored pending action
- registry entry is still compatible
- risk policy still allows confirmation
- guard policy still allows confirmation
- redaction policy is still valid
- external call flag is false for the current phase
- write intent flag is compatible with current Release Gate state
- target resource is still available if the proposal references one
- duplicate or replay attack checks pass
- idempotency key is present when required
- idempotency status is not conflict

Failure behavior:

- Missing pending action: `not_found`.
- Cross-user access: `not_found` or safe `forbidden_cross_user`, without
  leaking resource existence.
- Session mismatch: `confirmation_blocked`.
- Expired action: `expired`.
- Terminal conflict: existing terminal status.
- Tool version mismatch: `confirmation_blocked`.
- Input or preview hash mismatch: `confirmation_blocked`.
- Policy or guard denial: `rejected` or `confirmation_blocked`, depending on
  whether the proposal is unsafe or the request is invalid.
- Write not allowed by current phase: `execution_blocked`.
- Release Gate closed: `execution_blocked`.
- Idempotency conflict: `confirmation_blocked`.

Any failure must stop before execution. Blocked results may include a sanitized
user-visible reason. Server audit may include more detailed reason refs, but
must not include secrets, raw prompts, full context, or raw provider payloads.

## 8. Confirmation UX Contract

The frontend confirmation surface should show only sanitized, user-visible
preview information.

The user may see:

- sanitized summary
- user-visible diff
- target resource description
- risk explanation
- expiration time
- validation status
- confirmation requirement
- blocked or expired reason when safe
- result status after confirm, cancel, reject, or block

Interaction rules:

- User can click confirm.
- User can click cancel.
- User can reject when the product surface supports explicit rejection.
- High-risk actions may require typed confirmation text.
- Expired actions should show a clear expired state and offer a safe way to
  regenerate preview.
- Blocked actions should show a safe reason and avoid exposing internal policy
  details.
- Confirmed-but-not-executed actions must say no data was written.

UX boundary:

- UI must not imply that preview is execution.
- UI must not imply that confirmed means written, sent, deleted, or updated.
- UI must not display server-only args.
- UI must not display secrets, tokens, raw context, or raw knowledge content.
- UI must not bypass server-side validation.
- Frontend is the confirmation entrypoint, not a trust boundary.

Recommended no-write wording:

```text
Confirmed for preview. No data was written.
```

Recommended execution-blocked wording:

```text
This action was confirmed, but execution is not enabled in the current safety
phase.
```

## 9. Trace / Audit Integration

Confirmation runtime must align with Phase 7.3.

### Required trace fields

Confirmation trace should include:

- `traceId`
- `requestId`
- `confirmationId`
- `previewId`
- `pendingActionId`
- `toolCallId`
- `adapterId`
- `toolName`
- `toolVersion`
- `eventType=confirmation_requested`
- `eventStage=confirm`
- `eventStatus`
- `userRef`
- `sessionRef`
- `confirmationStatus`
- `riskLevel`
- `policyDecision`
- `validationResult`
- `redactionResult`
- `blockedReason`
- `externalSideEffect=false`
- `writeIntent`
- `writesData=false`
- `noWrite=true`
- `releaseGateDecision`
- `idempotencyKeyRef`
- `error`

Additional trace events may include:

- `confirmation_validated`
- `confirmation_blocked`
- `write_blocked`
- `error_returned`

### Required audit fields

Confirmation audit should include:

- `auditId`
- `traceId`
- `correlationId`
- `requestId`
- `confirmationId`
- `actorType`
- `userRef`
- `sessionRef`
- `authContextRef`
- `toolName`
- `toolVersion`
- `adapterId`
- `registryEntryRef`
- `riskLevel`
- `policyDecision`
- `validationResultRef`
- `featureGateState`
- `releaseGateState`
- `pendingActionId`
- `pendingActionStatus`
- `inputHash`
- `previewHash`
- `idempotencyKeyRef`
- `redactionResult`
- `blockedReason`
- `sanitizedInputSummary`
- `sanitizedOutputSummary`
- `retentionClass`

Audit is required for every confirm attempt, including successful confirmation,
cancel, reject, expired, cross-user, cross-session, hash mismatch, version
mismatch, idempotency conflict, feature-gate blocked, and Release Gate blocked
attempts.

### Visibility classification

Client-visible:

- trace id
- confirmation id
- pending action id
- preview id
- safe status
- safe lifecycle status
- safe blocked reason
- expiration
- `previewOnly=true`
- `wroteData=false`

Server-only:

- adapter id
- registry entry reference
- schema refs
- input hash
- preview hash
- feature flag key
- release gate decision
- idempotency key reference
- future execution handoff token
- auth context reference
- session binding details

Audit-only:

- audit id
- retention class
- internal policy reason reference
- internal validation reason reference
- hashed subject references
- blocked mutating attempt details

Prohibited:

- raw secret
- raw prompt
- hidden system prompt
- raw auth claim
- full sensitive memory, document, email, or knowledge context
- third-party provider payload containing private content
- raw idempotency keys when replayable

## 10. Safety Boundary

Confirmation runtime confirms intent only. It does not execute real actions.

Rules:

- `confirmed` does not equal `executed`.
- `execution_ready` and `executed` are future states only.
- Future execution requires a separate Release Gate and explicit approval.
- Any real write must be separately approved.
- Any external API call must be explicitly approved.
- Any production configuration change must be separately approved.
- Phase 7.5 must not introduce provider pilot runs.
- Phase 7.5 must not generate real provider reports.
- Phase 7.5 must not connect a real Firestore write path.
- Phase 7.5 must not modify deployment, Cloud Run env, Firestore Rules, or MCP.
- Durable memory write remains disabled.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- `life_events` remains unwritten by Phase 7.5 work.

If a confirmation path is valid but execution is not allowed, the correct result
is `execution_blocked` with `previewOnly=true`, `wroteData=false`, trace, and
audit.

## 11. Future Stage Relationship

Recommended later path:

- Phase 7.6 Pending Action Store Design: normalize pending action fields,
  hashes, policy snapshots, TTL cleanup, redaction metadata, indexes, and audit
  references. Store or Rules changes still need separate review.
- Phase 7.7 Guarded Execution Design: define the write coordinator entry
  criteria, Release Gate checks, idempotency, compensation, rollback, and
  execution audit model. This should remain no-production-write unless
  explicitly approved.
- Phase 7.8 Offline Fixture / Mock Tool Regression: create fixture-based
  regression coverage for preview and confirmation without external API calls,
  production data, or provider pilots.
- Phase 8 Release Gate / Online Canary: future planning only. Online canaries,
  external providers, production writes, and env changes require explicit user
  approval.

Phase 7.5 stops at confirmation contract design. It does not start pending
action store migration, guarded execution implementation, mock regression, or
online canary work.

## 12. Verification Plan

Phase 7.5 verification is docs-only:

- `git diff --stat`
- `git diff --check`
- `git status --short`

Full runtime tests are not required when the diff is documentation-only and no
API, frontend, runtime, schema, deployment, env, Firestore Rules, MCP, external
provider, or production data path changed.

## 13. Closeout Criteria

Phase 7.5 is complete when:

- `docs/phase7_5_confirmation_runtime_contract.md` exists.
- Confirmation request and response contracts are defined.
- Client-provided, server-only, recomputed, client-returnable, and prohibited
  fields are classified.
- Confirmation state machine is defined.
- Pre-confirmation validation rules are explicit.
- Confirmation UX contract is defined.
- Trace / audit alignment with Phase 7.3 is defined.
- Safety boundary is explicit.
- Future stage relationship is documented without entering those stages.
- Verification passes.
- A local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.5 defines how user confirmation should bind to a
server-side pending action and fail closed before execution. It does not enable
real tool actions, durable memory write, Firestore Memory runtime, `life_events`
writes, external provider calls, deployment changes, Firestore Rules changes,
MCP, provider pilots, or production data mutation.

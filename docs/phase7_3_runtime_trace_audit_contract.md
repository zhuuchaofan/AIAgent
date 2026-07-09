# Phase 7.3 Runtime Trace / Audit Contract

Date: 2026-07-08

## 1. Background

Phase 7.0 defined the Runtime Tooling Architecture for safe multi-tool Agent
expansion. Phase 7.1 froze the Tool Registry Contract, including risk
classification, feature flag binding, preview / confirm / write support,
auth boundaries, trace / audit requirements, error contract binding, and
versioning. Phase 7.2 defined the read-only runtime tool adapter contract and
the no-write execution boundary for future read-only tools.

Phase 7.3 narrows the trace and audit model into a user-visible Tool Execution
Trace / Audit Contract. The purpose is to make future tool behavior
understandable, reviewable, and debuggable without exposing secrets or granting
new execution authority.

This phase is design-only. It does not add trace code, audit storage,
frontend UI, runtime adapters, feature flag changes, durable writes,
deployments, Cloud Run environment changes, Firestore Rules changes, MCP
enablement, or external tool calls.

## 2. Goal

Phase 7.3 defines:

- user-visible tool execution trace fields
- server-side audit event fields
- event models for read-only, preview, confirm, and write stages
- sensitive information redaction rules
- relationship with feature gates
- relationship with pending actions
- relationship with idempotency
- relationship with the shared error contract
- frontend-visible versus backend-only boundaries
- forbidden trace and audit behavior
- completion standards for later implementation

The goal is not to log more data. The goal is to expose the minimum safe facts
needed to explain what the Agent considered, skipped, previewed, confirmed, or
executed.

## 3. Non-goals

Phase 7.3 does not:

- implement trace persistence
- implement audit persistence
- modify API runtime behavior
- modify frontend behavior
- add new endpoints
- create pending actions
- execute confirm paths
- execute writes
- write `users/{userId}/memories`
- write `life_events`
- connect durable memory write
- connect real Firestore Memory runtime
- modify Cloud Run environment variables
- modify Firestore Rules
- deploy
- enable MCP
- call external side-effect tools
- enable agent write flags
- enable memory proposal flags
- enable mock auth or mock LLM
- expose raw prompts, tokens, credentials, or secrets

Trace / audit design must not be treated as trace / audit availability. Future
implementation still requires separate code review, tests, feature gates, and
Release Gates for production-impacting writes.

## 4. Contract Layers

The runtime should distinguish three related but separate layers.

### User-visible trace

User-visible trace explains Agent behavior in product surfaces.

It should answer:

- which tool was considered
- which tool was selected or skipped
- why a feature gate blocked execution
- whether the result was read-only
- whether a preview was produced
- whether confirmation is required
- whether a write was skipped or executed
- what the safe next step is
- which error category occurred, if any

User-visible trace must be sanitized. It should prefer short explanations,
stable tool display names, timestamps, status, and safe summaries.

### Operator trace

Operator trace supports debugging and observability.

It may include more structured metadata than the user-visible trace, such as
correlation ids, registry version, timeout state, retry count, no-write reason,
schema refs, and sanitized input / output summaries. It must still avoid raw
secrets and full sensitive content by default.

### Audit record

Audit records support security review, compliance review, incident
investigation, and irreversible action review.

Audit is required for sensitive user data reads, preview-producing behavior,
confirm attempts, write attempts, blocked write attempts, external side-effect
attempts, cross-user failures, idempotency conflicts, and critical errors.

Audit records are not a frontend API. They may contain internal references, but
they must still follow redaction rules.

## 5. Trace Field Contract

Every considered or executed tool should produce a trace event. A future
implementation may group events under a single request trace.

Required trace fields:

- `traceId`
- `correlationId`
- `eventId`
- `eventType`
- `eventStage`
- `eventStatus`
- `occurredAt`
- `toolName`
- `toolVersion`
- `displayName`
- `category`
- `capabilityType`
- `riskLevel`
- `featureFlagKey`
- `featureGateState`
- `authResolved`
- `userScoped`
- `userRef`
- `inputSummary`
- `outputSummary`
- `noWrite`
- `writesData`
- `externalSideEffect`
- `pendingActionCreated`
- `pendingActionId`
- `confirmationRequired`
- `idempotencyKeyRef`
- `durationMs`
- `retryCount`
- `timeoutState`
- `error`
- `userVisibleMessage`

Field rules:

- `userRef` should be an internal reference or hash, not raw auth claims.
- `inputSummary` must be sanitized and should not contain full user content by
  default.
- `outputSummary` must be sanitized and should not contain full retrieved
  memory, document, email, or external content by default.
- `idempotencyKeyRef` should be a hash or internal reference, not a raw key when
  the key is sensitive or replayable.
- `pendingActionId` may be user-visible only when the product surface needs to
  reference the pending action.
- `error` should follow the shared error contract and should separate
  user-visible explanation from internal details.

## 6. Audit Field Contract

Audit records should be more stable than trace records because they may be used
for later review.

Required audit fields:

- `auditId`
- `traceId`
- `correlationId`
- `requestId`
- `eventType`
- `eventStage`
- `eventStatus`
- `occurredAt`
- `actorType`
- `userRef`
- `authContextRef`
- `toolName`
- `toolVersion`
- `contractVersion`
- `registryEntryRef`
- `category`
- `capabilityType`
- `riskLevel`
- `featureFlagKey`
- `featureGateState`
- `releaseGateName`
- `releaseGateState`
- `pendingActionId`
- `pendingActionStatus`
- `idempotencyKeyRef`
- `idempotencyStatus`
- `readsData`
- `writesData`
- `externalSideEffect`
- `resourceRefs`
- `sanitizedInputSummary`
- `sanitizedOutputSummary`
- `decisionReason`
- `errorCategory`
- `errorRef`
- `retentionClass`

Audit rules:

- Audit must be append-oriented. Later corrections should add new audit events
  instead of rewriting history.
- Audit must preserve the server-resolved user boundary.
- Audit must not trust frontend-provided `userId`.
- Audit must record blocked mutating attempts, not only successful writes.
- Audit must record write no-ops caused by disabled gates.
- Audit must record cross-user failures using safe generic user-visible
  messages and internal references only.
- Audit must not store raw credentials, tokens, prompt secrets, raw auth claims,
  or secret environment variables.

## 7. Event Type Model

Recommended event types:

- `tool_considered`
- `tool_selected`
- `tool_skipped`
- `feature_gate_checked`
- `input_validated`
- `read_executed`
- `preview_generated`
- `pending_action_created`
- `confirmation_requested`
- `confirmation_validated`
- `write_blocked`
- `write_started`
- `write_completed`
- `write_failed`
- `external_side_effect_blocked`
- `error_returned`
- `fallback_used`

Recommended event stages:

- `read_only`
- `preview`
- `confirm`
- `write`
- `external_boundary`
- `fallback`
- `error`

Recommended event statuses:

- `considered`
- `selected`
- `skipped`
- `blocked`
- `succeeded`
- `failed`
- `partial`
- `expired`
- `cancelled`
- `no_op`

Event names are product and runtime contracts. Future implementation should add
new event names additively and avoid renaming existing event names without a
compatibility plan.

## 8. Read-only Event Model

Read-only tool execution should produce trace for consideration, feature gate,
execution, sanitization, and final result.

Expected read-only flow:

1. `tool_considered`
2. `feature_gate_checked`
3. `input_validated`
4. `tool_selected` or `tool_skipped`
5. `read_executed`
6. `error_returned` or sanitized result event

Read-only trace requirements:

- `noWrite=true`
- `writesData=false`
- `externalSideEffect=false`
- `pendingActionCreated=false`
- `confirmationRequired=false`
- `featureGateState` recorded before provider execution
- sanitized input summary
- sanitized output summary
- duration, retry count, and timeout state
- error category when applicable

Read-only audit is required when the tool reads sensitive user data, including
RAG content, memory retrieval, timeline retrieval, daily summary retrieval, or
other user-scoped private content.

Read-only trace must not create pending actions, trigger preview proposals, call
confirm paths, or write durable data.

## 9. Preview Event Model

Preview-producing tools should trace proposal generation without durable
mutation.

Expected preview flow:

1. `tool_considered`
2. `feature_gate_checked`
3. `input_validated`
4. `tool_selected`
5. `preview_generated`
6. `pending_action_created`, if the preview is confirmable
7. `confirmation_requested`, if future write requires confirmation

Preview trace requirements:

- `noWrite=true`
- `writesData=false`
- `externalSideEffect=false`, unless a separately approved external preview
  boundary exists
- `pendingActionCreated=true` only when a server-side pending action is created
- `pendingActionId` included when needed for confirmation
- `confirmationRequired=true` before any future durable mutation
- guard and validation result summaries
- expiration and status summary for pending actions
- no-write reason when preview-only gates block writes

Preview audit is required for user-data-derived proposals, memory proposals,
life event proposals, task proposals, note proposals, and any preview that may
later become a confirmed write.

Preview must not be treated as consent to write.

## 10. Confirm Event Model

Confirm is the boundary where the user approves a server-side pending action.
Confirm must never trust a frontend-resubmitted payload as authority.

Expected confirm flow:

1. `confirmation_requested`
2. pending action loaded from server-side storage
3. ownership, status, expiration, action type, tool name, tool version, feature
   gate, release gate, and idempotency checks
4. `confirmation_validated` or structured error
5. `write_blocked` when gates or Release Gates do not allow mutation
6. `write_started` only when all write requirements are satisfied

Confirm trace requirements:

- server-resolved `userRef`
- `pendingActionId`
- pending action status
- pending action expiration state
- stored `toolName` and `toolVersion`
- feature gate state at confirm time
- release gate state at confirm time for write paths
- idempotency status
- decision reason
- error category when confirm fails

Confirm audit is required for every confirm attempt, including failed, expired,
cancelled, cross-user, already-consumed, idempotency-conflict, and feature-gate
blocked attempts.

Confirm must fail closed if the pending action cannot be loaded, does not
belong to the authenticated user, is expired, is cancelled, has already been
consumed, has incompatible schema versions, or lacks required gates.

## 11. Write Event Model

Write events are high-risk and must be traceable before, during, and after the
write coordinator call.

Expected write flow:

1. `confirmation_validated`
2. `write_started`
3. idempotent write coordinator execution
4. `write_completed`, `write_failed`, or `partial_failure`

Write trace requirements:

- `noWrite=false` only after a write is actually attempted
- `writesData=true`
- resource refs for mutated resources, when safe
- idempotency key reference
- idempotency status
- release gate state
- durable write result summary
- partial failure summary, if any
- compensation or retry guidance, if applicable

Write audit is required for:

- successful writes
- failed writes
- blocked writes
- partial failures
- idempotency conflicts
- retries
- compensation attempts

Write retries must never happen without idempotency. A disabled write gate
should produce a no-write trace and audit event with `write_blocked`,
`previewOnly=true` where applicable, and `wroteData=false`.

## 12. Sensitive Information Redaction

Trace and audit must apply redaction before persistence or frontend exposure.

Never record:

- access tokens
- refresh tokens
- API keys
- credentials
- cookies
- raw auth claims
- secret environment variables
- prompt secrets
- system prompts
- raw LLM provider payloads containing hidden instructions
- complete sensitive memory bodies by default
- complete sensitive document bodies by default
- complete email bodies by default
- external service secrets
- unredacted cross-user data

Prefer:

- ids
- hashes
- counts
- categories
- timestamps
- tool names and versions
- schema refs
- source refs
- redacted snippets
- user-visible summaries
- no-write and skipped reasons
- structured error categories

Redaction should happen before data leaves the runtime boundary. Sanitization
failure must fail closed and must not return or persist raw output.

## 13. Feature Gate Relationship

Feature gate state is part of trace and audit, but it is not authority by
itself.

Rules:

- Feature gates must be checked before tool execution.
- Feature gate state must be traced for selected, skipped, blocked, and failed
  tool attempts.
- Feature gates cannot bypass auth.
- Feature gates cannot bypass server-side `userId` resolution.
- Feature gates cannot bypass preview requirements.
- Feature gates cannot bypass confirmation requirements.
- Feature gates cannot bypass idempotency requirements.
- Feature gates cannot bypass audit requirements.
- A read-only gate cannot enable write behavior.
- A broad Agent flag cannot enable durable memory write.
- MCP enablement must remain separate from normal Phase 7 trace work.

Disabled gate behavior should produce a safe `feature_disabled` or no-tool
fallback event and must not create hidden pending actions or writes.

## 14. Pending Action Relationship

Trace and audit should link preview, confirm, and write events through
`correlationId` and `pendingActionId`.

Pending action trace rules:

- Pending actions are created only by preview-producing flows that explicitly
  support confirmation.
- Pending actions must be server-side records.
- Pending actions must be bound to the authenticated user id.
- Confirm must reload pending actions from server-side storage.
- Confirm must not trust frontend-resubmitted preview payloads.
- Pending action status transitions must be audited.
- Expired, cancelled, invalid, incompatible, or already-consumed pending actions
  must fail closed.

Pending action trace should include safe summaries of:

- action type
- tool name and version
- expiration
- status
- confirmation requirement
- no-write state before confirm
- write gate state after confirm, if applicable

## 15. Idempotency Relationship

Idempotency is required for confirm-required writes and external side-effect
mutation. It may also be used for retry-safe preview or read-only flows, but it
must not be used to bypass confirmation.

Idempotency trace fields:

- `idempotencyKeyRef`
- `idempotencyStatus`
- `idempotencyScope`
- `firstSeenAt`
- `lastSeenAt`
- `replayOf`
- `conflictReason`

Recommended idempotency statuses:

- `not_required`
- `required_missing`
- `new`
- `replayed`
- `conflict`
- `consumed`

Rules:

- Raw idempotency keys should not be exposed when they are sensitive or
  replayable.
- A replayed successful write should return the original safe result summary.
- An idempotency conflict must not execute a second write.
- Write retries must go through the idempotent coordinator.
- Idempotency conflicts must be audited.

## 16. Error Contract Relationship

Trace and audit must bind errors to the shared Phase 7 error contract.

Shared error categories:

- `validation_error`
- `unauthorized`
- `forbidden_cross_user`
- `feature_disabled`
- `preview_only`
- `confirmation_required`
- `pending_action_not_found`
- `pending_action_expired`
- `idempotency_conflict`
- `tool_unavailable`
- `external_side_effect_blocked`
- `write_failed`
- `partial_failure`
- `timeout`
- `retry_exhausted`
- `provider_error`
- `output_sanitization_failed`
- `schema_mismatch`
- `unsupported_tool_category`

Error trace rules:

- User-visible messages should be clear and safe.
- Internal diagnostics should use references, not raw sensitive payloads.
- Auth and cross-user failures must fail closed.
- Sanitization failures must not expose raw output.
- Failed preview must not become a write.
- Failed confirm must not call the write coordinator.
- Failed write must not be retried unless idempotency allows it.
- Error categories should remain stable across frontend and backend consumers.

## 17. Frontend / Backend Visibility Boundary

Frontend-visible trace may include:

- tool display name
- stage
- status
- safe user-visible explanation
- feature unavailable message
- preview availability
- confirmation requirement
- pending action reference when needed
- no-write status
- write success or failure summary
- safe error category
- timestamp
- trace id for support reference

Backend-only trace / audit may include:

- registry entry reference
- schema refs
- hashed user reference
- auth context reference
- feature flag key
- release gate name and state
- idempotency key reference
- provider timing
- retry count
- timeout state
- resource refs
- internal error reference

Frontend must not receive:

- raw auth claims
- raw credentials
- tokens
- secret env values
- prompt secrets
- hidden system prompts
- raw provider payloads
- full sensitive memory, document, or email bodies by default
- cross-user references
- write authority fields
- internal release gate override details

Frontend trace is explanatory. Backend audit is authoritative.

## 18. Prohibited Behavior

Phase 7.3 prohibits:

- using trace as permission to execute a tool
- using audit as permission to write
- logging raw tokens or credentials
- logging prompt secrets
- exposing raw auth claims
- trusting frontend `userId`
- creating pending actions from read-only tools
- converting preview into write without confirmation
- confirming from frontend-resubmitted payloads
- retrying durable writes without idempotency
- hiding writes behind read-only trace labels
- omitting audit for blocked mutating attempts
- omitting audit for cross-user failures
- storing full sensitive content in trace by default
- exposing backend-only audit fields to frontend
- enabling MCP through trace / audit work
- changing Cloud Run environment variables
- changing Firestore Rules
- deploying
- writing production Firestore or Cloud Storage data

Trace and audit must make unsafe behavior visible; they must never make unsafe
behavior acceptable.

## 19. Relationship with Existing Phase 6 / Phase 7 Work

Phase 6 Memory Engine remains preview-only / default-off at the durable write
boundary.

Mapping:

- Memory retrieval trace is read-only trace.
- Memory context injection trace is context retrieval trace, not instruction
  authority.
- Memory proposal trace is preview trace.
- `save_memory_preview` remains proposal-only trace.
- Durable memory write trace is future confirm-required write trace and remains
  disabled.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- Memory proposal runtime must not write `life_events`.

Phase 7.2 read-only adapter trace fields remain compatible with this contract.
Phase 7.3 expands the shared model for preview, confirm, write, audit, and
frontend / backend visibility boundaries.

## 20. Completion Standards for Future Implementation

A future implementation of this contract is complete only when:

- every tool consideration emits trace
- every selected tool emits trace
- every skipped or blocked tool emits trace
- sensitive user data reads emit audit when required
- preview-producing tools emit preview trace and audit
- confirm attempts emit audit whether they pass or fail
- write attempts emit audit whether they are blocked, succeed, fail, or
  partially fail
- feature gate state is recorded before execution
- pending action ids link preview, confirm, and write stages
- idempotency status is recorded for write paths
- structured errors map to the shared error contract
- redaction happens before persistence or frontend exposure
- frontend-visible trace excludes backend-only fields
- tests prove no raw tokens, credentials, auth claims, prompt secrets, or full
  sensitive bodies are persisted by default
- tests prove disabled write gates produce no-write trace and audit
- tests prove read-only tools cannot create pending actions
- tests prove confirm cannot trust frontend-resubmitted payloads
- tests prove idempotency conflicts do not execute duplicate writes

These standards describe future implementation readiness only. They are not
implemented by Phase 7.3.

## 21. Verification Plan

Phase 7.3 verification is docs-only:

- docs-only diff
- no runtime code changed
- no frontend changed
- no deployment
- no Cloud Run env changed
- no Firestore Rules changed
- no MCP changed
- no flags enabled
- no durable memory write
- no real Firestore Memory runtime
- no `users/{userId}/memories` write
- no `life_events` write
- no pending action / confirm / write behavior changed

Recommended checks:

- `git diff --stat`
- `git diff -- docs/phase7_3_runtime_trace_audit_contract.md`
- `git status --short`
- `git diff --check`
- markdown lint only if an existing project command is available

## 22. Closeout Criteria

Phase 7.3 is complete when:

- `docs/phase7_3_runtime_trace_audit_contract.md` exists.
- The document is consistent with Phase 7.0, Phase 7.1, Phase 7.2, and the
  Phase 6 to Phase 7 handoff.
- User-visible trace fields are defined.
- Server-side audit fields are defined.
- Read-only, preview, confirm, and write event models are defined.
- Sensitive information redaction rules are explicit.
- Feature gate, pending action, idempotency, and error contract relationships
  are explicit.
- Frontend-visible and backend-only boundaries are explicit.
- Prohibited behavior is explicit.
- Completion standards for future implementation are documented.
- No runtime code, frontend code, deployment configuration, Firestore Rules, MCP
  configuration, or production data writes are changed.
- Local docs-only commit is created when the user approves it.
- No push is performed.

## 23. Phase 7.3 Boundary and Next Step

Phase 7.3 defines the Runtime Trace / Audit Contract only. It does not implement
trace persistence, audit persistence, frontend trace UI, preview adapters,
confirm adapters, write coordinators, durable memory persistence, MCP tools, or
external side-effect tools.

Recommended next step: continue docs-first with Phase 7.4 Preview Tool Adapter
Design. That phase should define how preview-producing tools create sanitized
previews and server-side pending actions while preserving the no-write boundary
until explicit confirmation and future Release Gate approval.

Final conclusion: Phase 7.3 makes future Agent tool execution observable and
reviewable by contract. It does not enable new tool behavior, durable writes,
production memory persistence, deployment changes, Cloud Run environment
changes, Firestore Rules changes, MCP enablement, or autonomous mutation.

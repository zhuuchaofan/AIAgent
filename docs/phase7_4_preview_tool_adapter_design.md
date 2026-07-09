# Phase 7.4 Preview Tool Adapter Design

Date: 2026-07-09

## 1. Background

Phase 7.0 defined the Runtime Tooling Architecture for safe multi-tool Agent
expansion. Phase 7.1 defined the Tool Registry contract. Phase 7.2 defined and
later implemented a minimal read-only runtime adapter skeleton. Phase 7.3
defined the Runtime Trace / Audit Contract for read-only, preview, confirm, and
write stages.

Phase 6 Memory Engine is closed for preview-only / default-off implementation.
Durable memory write remains disabled, the real Firestore Memory runtime is not
connected, `users/{userId}/memories` is not written, and memory proposal confirm
still returns `previewOnly=true` / `wroteData=false`.

Phase 7.4 defines the contract for preview-producing tool adapters. A preview
tool may prepare a user-visible proposal and a server-side pending action, but
it must not perform durable business writes, external side effects, deployment
changes, or production configuration changes.

This document is design-only. It does not implement adapter code, change API
runtime behavior, change frontend behavior, change feature flags, deploy, or
call external providers.

## 2. Goals

Phase 7.4 defines:

- preview-producing tool adapter input and output contracts
- sanitized preview payload structure
- server-side pending action design boundaries
- separation between preview, confirmation, and future execution
- trace / audit integration with Phase 7.3
- boundaries between read-only tools, preview tools, and future write-capable
  tools
- field visibility across client-visible, server-only, audit-only, and
  prohibited data
- no-write / default-off / Release Gate requirements for preview tools

The goal is to let future tools explain what they intend to do before anything
is executed. Preview is a review surface, not execution authority.

## 3. Non-goals

Phase 7.4 does not:

- implement preview adapter runtime code
- enable durable memory write
- connect real Firestore Memory runtime
- write `users/{userId}/memories`
- write `life_events`
- create new production write paths
- execute confirm paths
- execute write coordinators
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

If a future task requires real writes, external calls, secrets, deployment,
Cloud Run environment changes, Firestore Rules changes, MCP, or production data
mutation, that task must stop for explicit user approval.

## 4. Tool Category Boundary

### Read-only tools

Read-only tools can execute after auth, registry, feature gate, user scope,
input validation, timeout, and sanitization checks. They may read user-scoped
data when explicitly allowed, but they must not create pending actions.

Required state:

- `noWrite=true`
- `writesData=false`
- `externalSideEffect=false`
- `pendingActionCreated=false`
- `confirmationRequired=false`

### Preview-producing tools

Preview tools produce a sanitized preview and may create a server-side pending
action for later confirmation. They cannot execute the proposed action.

Required state:

- `noWrite=true`
- `writesData=false`
- `externalSideEffect=false`
- `pendingActionCreated=true` only when the adapter stores a pending action
- `confirmationRequired=true` when future mutation could occur
- `previewOnly=true`

Examples include life event proposal, memory proposal, task proposal, note
proposal, future calendar event preview, and future document update preview.

### Future write-capable tools

Write-capable tools are not enabled by this phase. They require:

- server-side pending action lookup
- explicit confirmation
- user, session, TTL, risk, tool version, and input hash revalidation
- idempotency
- audit
- feature gates
- Release Gate approval before production writes

The preview adapter may produce a future execution handoff descriptor, but that
descriptor is not permission to execute.

## 5. Preview Tool Adapter Contract

A future preview adapter should be a server-side runtime boundary with this
conceptual interface:

```text
PreviewToolAdapter.createPreview(request, context) -> PreviewToolResponse
```

### Preview request

Required conceptual fields:

- `toolName`
- `toolVersion`
- `adapterId`
- `inputSchemaRef`
- `input`
- `requestId`
- `traceId`
- `correlationId`
- `toolCallId`
- `sessionRef`
- `userRef`, resolved from server-side auth context
- `featureGateState`
- `riskPolicy`
- `idempotencyKey`
- `requestedTtl`

Rules:

- `userRef` is authority only when resolved by the server.
- Client-provided `userId`, LLM-provided `userId`, and tool input `userId` are
  untrusted.
- `input` describes intent and candidate fields. It does not grant write
  authority.
- The adapter must validate `input` against `inputSchemaRef` before preview
  generation.
- The adapter must fail closed on schema mismatch, unsupported tool category,
  disabled feature gate, expired session, or unresolved auth.

### Preview response

Required conceptual fields:

- `toolName`
- `toolVersion`
- `adapterId`
- `previewId`
- `pendingActionId`
- `actionType`
- `previewOnly`
- `confirmationRequired`
- `riskLevel`
- `policyDecision`
- `sanitizedPreview`
- `pendingActionDescriptor`
- `traceContext`
- `auditMetadata`
- `redactionMetadata`
- `expiresAt`
- `idempotencyKeyRef`
- `futureExecutionHandoff`
- `error`

Response rules:

- `previewOnly` must be `true`.
- `confirmationRequired` must be `true` when the proposal could later mutate
  durable user data or an external system.
- `pendingActionId` may be returned to the client only as an opaque reference.
- `pendingActionDescriptor` must be stored server-side when confirmation is
  possible.
- `futureExecutionHandoff` must be server-only and blocked until a future
  confirmation and Release Gate path approves execution.
- Errors must use the shared error contract from Phase 7.3.

## 6. Field Visibility Contract

### Client-visible fields

The client may receive:

- `previewId`
- opaque `pendingActionId`
- tool display name
- action title
- safe summary
- user-visible diff
- target resource description
- risk explanation
- confirmation requirement
- expiration time
- validation status
- blocked reason, if safe
- trace id for support reference
- safe error category

Client-visible fields are explanatory only. They do not carry write authority.

### Server-only fields

Server-side pending action storage may keep:

- raw validated tool input when it is required for future confirmation
- input hash
- schema refs
- tool version
- adapter id
- registry entry ref
- auth context ref
- user scope ref
- session ref
- risk policy decision
- feature gate state
- idempotency key reference
- future execution handoff descriptor
- redaction metadata
- TTL and expiration metadata

Server-only fields must still avoid storing credentials, tokens, prompt secrets,
raw auth claims, secret environment variables, or unnecessary full sensitive
content.

### Audit-only fields

Audit may include:

- audit id
- trace id
- correlation id
- request id
- actor type
- user hash or subject reference
- auth context reference
- tool name and version
- adapter id
- registry entry reference
- risk level
- policy decision
- feature gate state
- pending action id
- pending action status
- input hash
- idempotency key reference
- redaction result
- blocked reason
- retention class

Audit is append-oriented. It is not a frontend API.

### Prohibited fields

The adapter must not place these in trace, audit, pending action payload, or
client response:

- access tokens
- refresh tokens
- API keys
- credentials
- cookies
- raw auth claims
- secret environment variables
- prompt secrets
- hidden system prompts
- raw external provider payloads
- complete sensitive memory bodies by default
- complete sensitive document bodies by default
- complete email bodies by default
- unredacted cross-user data
- raw idempotency keys when replayable
- local case data or knowledge context sent to third-party providers

When a preview cannot be safely redacted, the adapter must fail closed with
`output_sanitization_failed`.

## 7. Pending Action Design

Pending actions are server-side concepts. Existing production shape stores
pending actions under:

```text
users/{userId}/agent_pending_actions/{actionId}
```

Phase 7.4 does not change that path and does not add a new write target. It
defines the future contract for preview-producing adapters that create pending
actions.

### Pending action fields

Recommended conceptual fields:

- `pendingActionId`
- `previewId`
- `userRef`
- `sessionRef`
- `toolName`
- `toolVersion`
- `adapterId`
- `actionType`
- `status`
- `previewOnly`
- `confirmationRequired`
- `riskLevel`
- `policyDecision`
- `featureGateStateAtPreview`
- `inputSchemaRef`
- `inputHash`
- `sanitizedPreview`
- `serverOnlyExecutionDescriptor`
- `idempotencyKeyRef`
- `createdAt`
- `updatedAt`
- `expiresAt`
- `confirmedAt`
- `cancelledAt`
- `rejectedAt`
- `expiredAt`
- `executionBlockedAt`
- `executedAt`, future state only

### State machine

Recommended states:

- `preview_created`: adapter generated a sanitized preview.
- `confirmation_required`: user confirmation is required before any future
  mutation.
- `confirmed`: user approved the server-side pending action reference.
- `cancelled`: user cancelled the pending action.
- `expired`: TTL elapsed before confirmation.
- `rejected`: policy, validation, user decision, or safety guard rejected the
  proposal.
- `execution_blocked`: confirm passed or was attempted, but feature gate,
  Release Gate, risk policy, idempotency, or execution policy blocked mutation.
- `executed`: future state only; not enabled by Phase 7.4.

State rules:

- `preview_created` may transition to `confirmation_required`, `rejected`, or
  `expired`.
- `confirmation_required` may transition to `confirmed`, `cancelled`,
  `rejected`, or `expired`.
- `confirmed` may transition to `execution_blocked` in preview-only phases.
- `confirmed` may transition to `executed` only in a future Release Gate
  approved write path.
- Terminal decisions must be idempotent.
- Conflicting terminal decisions must fail closed.

### Confirmation revalidation

Confirm must reload the pending action from server-side storage and revalidate:

- authenticated user ownership
- session binding, when required
- TTL and expiration
- status and terminal decision state
- tool name and tool version
- adapter id
- registry entry compatibility
- feature gate state at confirm time
- risk policy
- input hash
- idempotency key reference
- Release Gate state for future execution

Confirm must not trust frontend-resubmitted preview payloads or execution args.
The client may submit only the opaque pending action reference and an explicit
confirmation decision.

## 8. Sanitized Preview Design

The sanitized preview is the user-facing explanation of the proposed action. It
must be sufficient for informed confirmation without leaking secrets or full
sensitive context.

Recommended structured shape:

```json
{
  "summary": "Create a life event about today's observation.",
  "userVisibleDiff": [
    {
      "operation": "create",
      "field": "title",
      "safeValue": "Black cat observation",
      "redacted": false
    }
  ],
  "targetResource": {
    "resourceType": "life_event",
    "description": "A private life event in the authenticated user's account"
  },
  "riskExplanation": "This would create private user data after confirmation.",
  "fieldsToCreate": ["title", "content", "tags"],
  "fieldsToUpdate": [],
  "fieldsToDelete": [],
  "redactedValues": [
    {
      "field": "sourceText",
      "reason": "contains sensitive source context"
    }
  ],
  "irreversibleWarning": null,
  "requiredConfirmationText": "Confirm to continue. No data is written in the current phase.",
  "confidence": 0.82,
  "validationResult": {
    "status": "passed",
    "warnings": []
  },
  "blockedReason": null,
  "markdown": "Safe human-readable preview text."
}
```

Rules:

- Preview must not claim an action has already run.
- Preview must not produce durable business side effects.
- Preview must not write `users/{userId}/memories` or `life_events`.
- Preview must not call external APIs unless a separate user-approved external
  preview boundary exists.
- Preview must not expose complete secrets, tokens, PII, customer data, local
  case material, or raw knowledge context.
- Preview should support structured JSON and markdown consumption.
- Markdown is a rendering aid; structured JSON remains the authoritative
  contract.
- Redacted values should explain that redaction occurred without exposing the
  hidden value.
- Low-confidence or policy-blocked previews should show `blockedReason` and
  must not create executable authority.

## 9. Trace / Audit Integration

The preview adapter must align with Phase 7.3.

### Required trace fields

Preview trace should include:

- `traceId`
- `requestId`
- `correlationId`
- `toolCallId`
- `adapterId`
- `previewId`
- `pendingActionId`
- `toolName`
- `toolVersion`
- `eventType=preview_generated`
- `eventStage=preview`
- `eventStatus`
- `userRef`
- `riskLevel`
- `policyDecision`
- `redactionResult`
- `featureGateState`
- `externalSideEffect=false`
- `writeIntent=true` when the proposal could later write
- `writesData=false`
- `noWrite=true`
- `confirmationRequired`
- `confirmationStatus`
- `blockedReason`
- `error`

### Required audit fields

Preview audit should include:

- `auditId`
- `traceId`
- `correlationId`
- `requestId`
- `actorType`
- `userRef`
- `authContextRef`
- `toolName`
- `toolVersion`
- `adapterId`
- `registryEntryRef`
- `riskLevel`
- `policyDecision`
- `featureGateState`
- `pendingActionId`
- `pendingActionStatus`
- `inputHash`
- `idempotencyKeyRef`
- `redactionResult`
- `sanitizedInputSummary`
- `sanitizedOutputSummary`
- `blockedReason`
- `retentionClass`

### Visibility classification

Client-visible:

- `traceId`
- `previewId`
- opaque `pendingActionId`
- safe tool display name
- preview status
- safe policy or blocked explanation
- confirmation requirement

Server-only:

- `adapterId`
- registry entry reference
- schema refs
- input hash
- feature flag key
- idempotency key reference
- future execution handoff
- auth context reference

Audit-only:

- audit id
- retention class
- internal decision reason
- internal error reference
- hashed subject reference
- blocked mutating attempt details

Prohibited:

- raw secret
- raw prompt
- hidden system prompt
- raw auth claim
- full sensitive memory, document, email, or knowledge context
- third-party provider payload containing private content

## 10. Safety Boundary

Preview-producing tools can prepare actions, but they cannot execute actions.

Rules:

- Any real write must go through confirm plus a separately approved Release
  Gate.
- Any external API call requires explicit user approval and a dedicated boundary
  design.
- Any production configuration change requires separate approval.
- Phase 7.4 must not introduce provider pilot runs.
- Phase 7.4 must not generate real provider reports.
- Phase 7.4 must not connect a real Firestore write path.
- Durable memory write remains disabled.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- `life_events` remains unwritten by preview adapter work.
- Cloud Run env remains unchanged.
- Firestore Rules remain unchanged.
- MCP remains unchanged.

Disabled gate behavior should produce a safe no-write result with trace and
audit, not a hidden fallback to execution.

## 11. Future Stage Relationship

Recommended later path:

- Phase 7.5 Confirmation Runtime Contract: define confirm request / response,
  revalidation, terminal state behavior, idempotent confirm, and fail-closed
  errors. This phase should not enable execution.
- Phase 7.6 Pending Action Store Design: normalize pending action storage,
  indexes, TTL cleanup, retention, redaction, and audit references. Firestore
  Rules or production store changes still need separate review.
- Phase 7.7 Guarded Execution Design: define write coordinator entry criteria,
  Release Gate checks, idempotency, compensation, and rollback. This remains
  no-production-write unless separately approved.
- Phase 7.8 Offline Fixture / Mock Tool Regression: create fixture-based tests
  for read-only and preview tools without external API calls or production data.
- Phase 8 Release Gate / Online Canary: future planning only. Online canaries,
  external providers, and production writes require explicit user approval.

Phase 7.4 should stop at preview adapter design. It should not directly enter
confirmation implementation, pending action migration, guarded execution, or
online canary work.

## 12. Verification Plan

Phase 7.4 verification is docs-only:

- `git diff --stat`
- `git diff --check`
- `git status --short`

Full runtime tests are not required when the diff is documentation-only and no
API, frontend, runtime, schema, deployment, env, Firestore Rules, MCP, or
production data path changed.

## 13. Closeout Criteria

Phase 7.4 is complete when:

- `docs/phase7_4_preview_tool_adapter_design.md` exists.
- Goals and non-goals are explicit.
- Preview Tool Adapter contract is defined.
- Pending action state machine is defined.
- Sanitized preview structure and redaction rules are defined.
- Trace / audit alignment with Phase 7.3 is defined.
- Safety boundary is explicit.
- Future phase relationship is documented without starting those phases.
- Verification passes.
- A local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.4 defines how preview-producing tools should prepare
safe, sanitized, reviewable proposals and server-side pending actions. It does
not enable durable writes, external provider calls, deployment changes,
Firestore Rules changes, MCP, real provider pilots, or production data mutation.

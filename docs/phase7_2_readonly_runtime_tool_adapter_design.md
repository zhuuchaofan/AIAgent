# Phase 7.2 Read-only Runtime Tool Adapter Design

Date: 2026-07-02

## 1. Background

Phase 7.0 defined the Runtime Tooling Architecture for safe multi-tool Agent
expansion. Phase 7.1 froze the Tool Registry Contract, including registry
metadata, risk classification, feature flag binding, auth boundaries,
preview / confirm / write support matrix, trace / audit requirements, and
versioning rules.

Phase 7.2 continues docs-first work by defining the future read-only runtime
tool adapter. This adapter is the runtime boundary that may execute
registry-declared read-only tools after auth, user scope, feature gate, input
validation, timeout, retry, sanitization, trace, and error handling checks.

The adapter is read-only by design. It must not write durable data, create
pending actions, require confirm, perform preview-producing behavior, or call
external side-effect tools.

This document is design-only. It does not implement the adapter.

## 2. Goal

Phase 7.2 defines:

- read-only adapter responsibilities
- how the adapter consumes Tool Registry metadata
- auth and `userId` trust boundary
- feature flag gate behavior
- trace and audit behavior
- error contract behavior
- timeout, retry, and fallback behavior
- sanitized output contract
- relationship with RAG retrieval, memory retrieval, and timeline retrieval
- preparation for a later minimal skeleton without writing code now

The goal is to make future read-only tool execution explicit, reversible,
observable, and no-write by contract.

## 3. Non-goals

Phase 7.2 does not:

- write adapter code
- implement Tool Registry runtime
- modify API runtime
- modify frontend
- create pending actions
- implement confirm
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

Read-only design must not be used as a shortcut to enable preview proposals,
confirmed writes, external mutation, deployment changes, or production
configuration changes.

## 4. Read-only Adapter Responsibilities

The future adapter should:

- resolve the tool entry from the registry
- validate that the tool category is read-only
- validate `capabilityType`
- validate authenticated server-side user context
- resolve `userId` from server-side auth context
- enforce `userScoped` boundaries
- enforce feature flag state
- validate input against the declared schema
- execute a read-only provider under timeout policy
- apply safe retry policy when allowed
- normalize provider output
- sanitize sensitive result content
- emit trace
- emit audit when required by the registry
- return a normalized read-only response
- return structured errors from the shared error contract

The adapter must never:

- create a pending action
- require confirmation
- write durable state
- trigger a preview proposal as a hidden side effect
- call a mutating provider
- call an external side-effect tool
- accept a client-supplied `userId` as authority

## 5. Runtime Flow

Future read-only adapter flow:

1. Receive Agent tool invocation request.
2. Resolve authenticated user context.
3. Resolve requested `toolName` and `toolVersion`.
4. Load matching Tool Registry entry.
5. Reject unsupported, missing, deprecated-unsafe, or non-read-only entries.
6. Check registry fields:
   - `category`
   - `capabilityType`
   - `readsData`
   - `writesData`
   - `externalSideEffect`
   - `supportsDirectExecute`
   - `supportsPreview`
   - `supportsConfirm`
7. Check feature flag state.
8. Validate auth and user scope.
9. Validate input schema.
10. Execute read-only provider under timeout policy.
11. Apply safe retry policy if allowed and needed.
12. Normalize provider result.
13. Sanitize sensitive content.
14. Validate output schema.
15. Emit trace and audit if required.
16. Return read-only result or structured error.

The entire flow has no preview proposal, no pending action, no confirm, no write,
and no external side effect.

## 6. Registry Consumption

The adapter should consume these Phase 7.1 registry fields:

- `toolName`
- `toolVersion`
- `category`
- `capabilityType`
- `riskLevel`
- `authRequired`
- `userScoped`
- `readsData`
- `writesData`
- `externalSideEffect`
- `supportsDirectExecute`
- `supportsPreview`
- `supportsConfirm`
- `featureFlagKey`
- `traceRequired`
- `auditRequired`
- `timeoutPolicy`
- `retryPolicy`
- `errorContractVersion`
- `inputSchemaRef`
- `outputSchemaRef`

Read-only eligibility rules:

- `category` must be `read_only_retrieval`, `diagnostics_only`, or an approved
  non-mutating `system_internal` category.
- `readsData` may be true.
- `writesData` must be false.
- `externalSideEffect` must be false.
- `supportsDirectExecute` may be true, but only for read-only execution.
- `supportsConfirm` must be false.
- `supportsPreview` should usually be false.
- If `supportsPreview` is true for a read-only helper, it must not create a
  pending action or durable proposal.

Registry consumption is defensive. A mismatched registry entry should fail
closed with `unsupported_tool_category`, `schema_mismatch`, or
`feature_disabled` rather than attempting best-effort execution.

## 7. Auth and User Scope

Read-only does not mean permission-free. User data reads still require trusted
identity and tenant isolation.

Rules:

- `userId` can only come from server-side auth context.
- Frontend payload `userId` is untrusted.
- LLM output `userId` is untrusted.
- Tool input `userId` is untrusted.
- The adapter must not accept a client override for `userId`.
- `userScoped` tools must bind provider queries to the authenticated user id.
- Cross-user requests must return `forbidden_cross_user`.
- Anonymous or unauthenticated requests must return `unauthorized`.
- Read-only providers must not fetch data outside the authenticated user's
  allowed scope.

For non-user-scoped diagnostics, the registry must explicitly declare
`userScoped=false`, `authRequired` expectations, and audit behavior. Diagnostics
must still avoid leaking production secrets or other users' data.

## 8. Feature Gate Strategy

Read-only tools may be default-off or controlled-rollout. The adapter should
check the registry's `featureFlagKey` before provider execution.

Rules:

- Feature disabled returns `feature_disabled`.
- Feature flags cannot bypass auth.
- Feature flags cannot bypass user scope.
- Feature flags cannot bypass trace requirements.
- Feature flags cannot upgrade a read-only tool into a write tool.
- The read-only adapter must not control durable write flags.
- Durable write flags must not be consulted as permission to read.
- A broad read-only flag should not implicitly enable sensitive memory or
  timeline retrieval unless the tool-specific flag also allows it.

Recommended disabled behavior:

- return a normalized no-tool result for optional context
- return a user-visible feature unavailable message for explicit tool requests
- emit trace explaining the feature gate state
- avoid creating pending actions or fallbacks to mutating tools

## 9. Trace and Audit

The adapter should emit trace for every tool considered or executed. Audit is
required when the registry entry says `auditRequired=true`, especially for
sensitive user data reads.

Trace fields:

- `traceId`
- `correlationId`
- `toolName`
- `toolVersion`
- `category`
- `capabilityType`
- `riskLevel`
- user id hash or internal reference, not raw sensitive data if policy requires
- `featureGateState`
- sanitized input summary
- sanitized output summary
- `noWrite=true`
- `writesData=false`
- `externalSideEffect=false`
- `pendingActionCreated=false`
- `confirmationRequired=false`
- `durationMs`
- timeout state
- retry count
- error code, if any

Trace rules:

- Do not record tokens.
- Do not record credentials.
- Do not record prompt secrets.
- Do not record full sensitive document body by default.
- Do not record full sensitive memory content by default.
- Do not record raw auth claims.
- Prefer ids, counts, categories, redacted snippets, and user-visible summaries.

Even for read-only tools, trace should explain tool selection, feature gate
state, read-only result status, and no-write conclusion.

## 10. Error Contract

Read-only adapter errors should use the shared contract from Phase 7.1 plus
read-only-specific categories where needed.

| Error | Retry | User-visible | Trace | Audit |
| --- | --- | --- | --- | --- |
| `validation_error` | No, until input changes | Yes | Yes | Optional |
| `unauthorized` | After re-auth | Yes | Yes | Yes |
| `forbidden_cross_user` | No | Generic message | Yes | Yes |
| `feature_disabled` | No | Yes | Yes | Optional |
| `tool_unavailable` | Maybe | Yes | Yes | Optional |
| `timeout` | Maybe if retry policy allows | Yes | Yes | Optional |
| `retry_exhausted` | No immediate retry | Yes | Yes | Optional |
| `provider_error` | Maybe | Yes | Yes | Optional |
| `output_sanitization_failed` | No | Generic message | Yes | Yes |
| `schema_mismatch` | No | Generic message | Yes | Yes |
| `unsupported_tool_category` | No | Yes | Yes | Yes |

Error rules:

- Auth and cross-user errors fail closed.
- Non-read-only categories fail closed.
- Provider failures must not fall back to writes.
- Sanitization failure must not return raw output.
- Schema mismatch must not return unchecked output.
- Timeout and retry behavior must follow registry policy.

## 11. Timeout / Retry / Fallback

Timeout policy comes from the registry entry. Retry policy also comes from the
registry entry.

Rules:

- Retry is allowed only for safe read-only providers.
- Retry must not change user data.
- Retry must not create pending actions.
- Retry must not call confirm or write paths.
- Retry count and total duration must be traced.
- Provider timeout should return `timeout` or `retry_exhausted`.

Fallback options:

- empty result
- partial result
- cached safe result when explicitly allowed
- no-tool fallback to ordinary Agent behavior
- user-visible explanation that the read-only tool was unavailable

Forbidden fallbacks:

- fallback to write tool
- fallback to preview proposal tool
- fallback to external side-effect tool
- fallback to cross-user data
- fallback to unredacted provider output

## 12. Sanitized Output Contract

The adapter should return a normalized result to the Agent.

Recommended result fields:

- `toolName`
- `toolVersion`
- `resultType`
- `items`
- `sourceSummary`
- `confidence`
- `retrievedAt`
- `isPartial`
- `noWrite=true`
- `writesData=false`
- `externalSideEffect=false`
- `traceId`
- `error`, if any

Sanitization requirements:

- Do not expose internal tokens.
- Do not expose raw auth claims.
- Do not expose secret environment variables.
- Do not expose full sensitive logs.
- Do not expose cross-user data.
- Do not expose full sensitive memory or document text unless explicitly allowed
  by the tool contract and user-visible product surface.
- Preserve enough citation, source, or memory id information for traceability
  without leaking unauthorized content.

For RAG retrieval, output should preserve citation/source references needed for
answer grounding. For memory retrieval, output should preserve memory ids,
types, relevance, and safe summaries without exposing hidden or out-of-scope
content.

## 13. Candidate Read-only Tools

Candidate tools for future adapter work:

- `rag_retrieval`
- `memory_retrieval`
- `timeline_retrieval`
- `summary_retrieval`
- `diagnostics_readonly`

Phase 7.2 only designs these candidates. It does not implement or enable them.

Candidate notes:

- `rag_retrieval` can use existing RAG retrieval concepts, but future adapter
  execution must still go through registry, auth, feature gate, trace, and
  sanitized output.
- `memory_retrieval` can use Phase 6.7A as a future candidate source, but real
  Firestore Memory runtime remains disconnected.
- `timeline_retrieval` and `summary_retrieval` must be user-scoped and must not
  write derived memory or proposals.
- `diagnostics_readonly` must avoid leaking production secrets.

## 14. Relationship with Phase 6

Phase 6.7A read-only memory retrieval minimal implementation can be a future
candidate source for the adapter, but Phase 7.2 does not connect it.

Current boundaries remain:

- Real Firestore Memory runtime is not connected.
- Durable memory write is not enabled.
- `users/{userId}/memories` is not written.
- Memory proposal runtime is not part of the read-only adapter.
- Memory proposal runtime must not write `life_events`.
- Timeline / summary extraction remains controlled and must not trigger hidden
  memory proposal generation from read-only retrieval.

The read-only adapter should treat memory retrieval as context retrieval only.
Retrieved memory must not become instruction authority and must not trigger
write behavior.

## 15. Security Boundaries

Phase 7.2 must preserve:

- no frontend `userId` trust
- no write
- no pending action
- no confirm
- no external side effect
- no durable memory write
- no `life_events` write
- no `users/{userId}/memories` write
- no Cloud Run env change
- no Firestore Rules change
- no MCP enablement
- no deployment mixed into this phase
- no mock auth or mock LLM enablement
- no registry runtime implementation
- no read-only adapter runtime implementation

Read-only execution must remain observable, gated, and tenant-safe. If the
adapter cannot prove a request is safe, it must no-op or fail closed.

## 16. Proposed Follow-up Phases

Suggested future work:

- Phase 7.3 Runtime Trace / Audit Contract
- Phase 7.4 Preview Tool Adapter Design
- Phase 7.5 Confirm-required Tool Contract Design
- Phase 7.6 External Tool / MCP Boundary Design
- Phase 7.7 Phase 7 closeout

These are recommendations only. This document does not implement them.

## 17. Verification Plan

Phase 7.2 verification is docs-only:

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
- no pending action / confirm / write added

Recommended checks:

- `git diff --stat`
- `git diff -- docs/phase7_2_readonly_runtime_tool_adapter_design.md`
- `git status --short`
- `git diff --check`
- markdown lint only if an existing project command is available

## 18. Closeout Criteria

Phase 7.2 is complete when:

- `docs/phase7_2_readonly_runtime_tool_adapter_design.md` exists.
- The document is consistent with Phase 7.0 and Phase 7.1.
- Read-only adapter responsibilities are clear.
- Registry consumption is clear.
- Auth and `userId` boundary are explicit.
- No-write / no-pending-action / no-confirm boundaries are explicit.
- Trace, error, timeout, retry, fallback, and sanitization strategy are clear.
- Follow-up phase recommendations are documented.
- Local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.2 defines the future read-only runtime tool adapter
contract. It does not implement the adapter, enable tools, create pending
actions, confirm actions, write data, connect durable memory, deploy, or modify
production configuration.

# Phase 7.1 Tool Registry Contract Design

Date: 2026-07-02

## 1. Background

Phase 7.0 defined the Runtime Tooling Architecture for safe multi-tool Agent
expansion. It established the high-level categories, preview / confirm / write
strategy, trace and audit direction, error handling expectations, and the
separation between normal development phases and Release Gates.

Phase 7.1 narrows that architecture into the Tool Registry contract. The Tool
Registry is the future runtime's central description layer for identifying,
constraining, tracing, auditing, and eventually executing tools. It describes
what a tool is allowed to do; it does not enable that tool by itself.

This phase is design-only. It does not add registry code, runtime adapters,
external tool calls, feature flag changes, durable writes, deployments, Cloud
Run environment changes, Firestore Rules changes, or MCP enablement.

## 2. Goal

Phase 7.1 freezes the registry contract needed by later runtime adapter phases.

Goals:

- define tool registration metadata
- define tool risk levels
- define tool capability declarations
- define feature flag binding
- define preview / confirm / write support matrix
- define auth and `userId` trust boundary requirements
- define trace / audit requirements
- define versioning / compatibility strategy
- provide a stable contract for Phase 7.2+ runtime adapter design

The registry contract should make future tool expansion explicit, reviewable,
and testable before any implementation changes runtime behavior.

## 3. Non-goals

Phase 7.1 does not:

- write registry code
- implement runtime adapters
- call real tools
- enable any writes
- connect durable memory write
- connect real Firestore Memory runtime
- write `users/{userId}/memories`
- write `life_events`
- modify API runtime
- modify frontend
- modify Firestore Rules
- modify Cloud Run environment variables
- deploy
- enable MCP
- connect external side-effect tools
- enable agent write flags
- enable memory proposal flags
- enable mock auth or mock LLM

Registry design must not be treated as registry availability. Future
implementation still requires separate phase approval, feature gates, tests, and
Release Gates for production-impacting behavior.

## 4. Registry Metadata Model

Each future registry entry should describe one tool capability through stable
metadata.

Required conceptual fields:

- `toolName`
- `displayName`
- `toolVersion`
- `ownerDomain`
- `description`
- `category`
- `capabilityType`
- `riskLevel`
- `authRequired`
- `userScoped`
- `readsData`
- `writesData`
- `externalSideEffect`
- `confirmationRequired`
- `supportsPreview`
- `supportsConfirm`
- `supportsDirectExecute`
- `supportsIdempotency`
- `featureFlagKey`
- `releaseGate`
- `traceRequired`
- `auditRequired`
- `timeoutPolicy`
- `retryPolicy`
- `errorContractVersion`
- `inputSchemaRef`
- `outputSchemaRef`
- `previewSchemaRef`
- `confirmationSchemaRef`

Optional lifecycle fields for a later implementation:

- `createdAt`
- `updatedAt`
- `deprecatedAt`
- `replacementToolName`
- `compatibilityNotes`

This document defines the conceptual contract only. It does not create JSON
schema files, code models, migrations, or runtime registration.

## 5. Tool Categories and Capability Types

The registry should support these categories:

| Category | Direct execute | Preview | Confirm | Write | External side effect | Default enabled |
| --- | --- | --- | --- | --- | --- | --- |
| `read_only_retrieval` | Allowed after gates | No | No | No | No | No, unless explicitly rolled out |
| `preview_proposal` | No durable execute | Required | Optional later | No by itself | No | No |
| `confirm_required_write` | No | Required | Required | Only after gate | Usually no | No |
| `external_side_effect` | Read-only only if scoped | Required for mutation | Required for mutation | Possible after gate | Yes | No |
| `system_internal` | Runtime-only | Case-specific | Case-specific | Case-specific | No | No |
| `diagnostics_only` | Allowed after gates | No | No | No | No | No, unless explicitly rolled out |

Capability type should describe the behavior more precisely than category:

- `retrieval`
- `status_check`
- `diagnostic`
- `proposal_generation`
- `pending_action_confirmation`
- `durable_write`
- `external_read`
- `external_write`
- `internal_transform`

Categories and capability types are guardrails for runtime routing. They must
not be used to bypass auth, feature flags, preview, confirmation, audit, or
Release Gate requirements.

## 6. Risk Classification

The registry should use explicit risk levels.

### `low_readonly`

Examples: non-sensitive status checks and safe diagnostics.

- Default enabled: no, unless explicitly rolled out.
- Confirmation required: no.
- Release Gate required: no for local/default-off implementation.
- Audit required: optional; trace required.
- Ordinary phase enablement: possible after design and tests.

### `medium_sensitive_read`

Examples: RAG retrieval, memory retrieval, timeline retrieval, daily summary
retrieval.

- Default enabled: no.
- Confirmation required: no, but user-visible trace is required.
- Release Gate required: not for read-only integration, unless production scope
  or external data access changes.
- Audit required: yes for user data reads.
- Ordinary phase enablement: possible only with auth scoping, redaction, and
  no-write verification.

### `medium_preview_only`

Examples: life event proposal, memory proposal, task proposal, note proposal.

- Default enabled: no.
- Confirmation required: required before any future write.
- Release Gate required: no for preview-only proposal generation, yes before
  durable write.
- Audit required: yes.
- Ordinary phase enablement: possible only as no-write preview behavior.

### `high_internal_write`

Examples: confirmed `life_event` write, confirmed task / reminder write.

- Default enabled: no.
- Confirmation required: yes.
- Release Gate required: yes for production real writes.
- Audit required: yes.
- Ordinary phase enablement: design or default-off code only; no production
  write enablement.

### `high_external_side_effect`

Examples: calendar write, file write, email draft mutation, third-party API
mutation.

- Default enabled: no.
- Confirmation required: yes.
- Release Gate required: yes.
- Audit required: yes.
- Ordinary phase enablement: boundary design only unless explicitly approved.

### `critical_release_gated`

Examples: durable memory write, email send, destructive file operations,
cross-system write tools, MCP mutating tools.

- Default enabled: no.
- Confirmation required: yes.
- Release Gate required: yes.
- Audit required: yes.
- Ordinary phase enablement: prohibited without dedicated Release Gate approval.

## 7. Feature Flag Binding

Every registry entry should declare its controlling feature flag or explicitly
state that it is not runtime-available.

Binding rules:

- Read-only tools may use default-off flags or controlled rollout flags.
- Preview tools must be disable-able by feature flag.
- Write tools must be default-off.
- Durable memory write must use an independent Release Gate and dedicated flag.
- External side-effect tools must use independent Release Gates.
- MCP enablement must use a separate Release Gate and must not be folded into a
  broad tool flag.

Feature flags control whether a capability can be considered or executed. They
must not bypass:

- auth
- server-side `userId` resolution
- user scope checks
- preview requirements
- confirmation requirements
- idempotency
- audit
- Release Gate requirements

Recommended binding fields:

- `featureFlagKey`
- `defaultEnabled`
- `rolloutMode`
- `requiresReleaseGate`
- `releaseGateName`
- `disabledBehavior`
- `safeFallback`

## 8. Preview / Confirm / Write Support Matrix

All Phase 7.1 entries below are design-only. None are implemented or enabled by
this document.

| Tool | Direct execute | Preview required | Confirm required | Write allowed | External side effect | Release gated | Phase 7.1 implementation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RAG retrieval | Yes, after gates | No | No | No | No | No | No |
| Memory retrieval | Yes, after gates | No | No | No | No | No for read-only, yes for production scope changes | No |
| Timeline retrieval | Yes, after gates | No | No | No | No | No | No |
| Life event proposal | No durable execute | Yes | Before any write | No by itself | No | Write path yes | No |
| Memory proposal | No durable execute | Yes | Before any write | No by itself | No | Durable write yes | No |
| Task proposal | No durable execute | Yes | Before any write | No by itself | No | Write path yes | No |
| Life event write | No | Yes | Yes | Only after gate | No | Yes | No |
| Memory durable write | No | Yes | Yes | Only after gate | No | Yes | No |
| Calendar write | No | Yes | Yes | Only after gate | Yes | Yes | No |
| Email send | No | Yes | Yes | Only after gate | Yes | Yes | No |
| File write | No | Yes | Yes | Only after gate | Yes | Yes | No |
| MCP tool call | No by default | Case-specific | Case-specific | Not in normal phase | Possible | Yes | No |

Matrix rules:

- "Direct execute" never means "skip auth."
- "Preview required" means the user sees a structured proposal before any
  durable mutation.
- "Confirm required" means confirm references a server-side pending action.
- "Write allowed" requires feature flags and Release Gate approval.
- External side effects require dedicated boundary design before implementation.

## 9. Auth and User Scope Contract

The registry can declare that a tool is user-scoped, but it must not resolve
`userId` from client input.

Auth contract:

- `userId` can only come from server-side auth context.
- Frontend payload `userId` is untrusted.
- LLM output `userId` is untrusted.
- Tool input `userId` is untrusted.
- Request body, query string, and headers outside the verified auth context are
  untrusted for protected user identity.

Pending action contract:

- Confirm must reference a server-side pending action.
- Pending actions must be bound to the authenticated user id.
- Confirm must reload the pending action from server-side storage.
- Confirm must validate ownership, status, expiration, action type, tool name,
  tool version, idempotency, and feature gates.
- Cross-user confirm or write must return `forbidden_cross_user`.
- Expired, cancelled, invalid, or already-consumed pending actions must not
  write.

Registry entries should declare:

- `authRequired`
- `userScoped`
- `allowedAuthModes`
- `requiresServerResolvedUserId`
- `crossUserBehavior`
- `pendingActionRequiredForConfirm`

## 10. Trace and Audit Requirements

The registry should declare whether trace and audit are required for each tool.

Required registry declarations:

- `traceRequired`
- `auditRequired`
- trace fields
- audit event type
- no-write reason
- skipped reason
- feature gate state
- `riskLevel`
- `toolVersion`
- request `correlationId`
- `pendingActionId`, if any
- `idempotencyKey`, if any
- sanitized input summary
- sanitized output summary
- error category

Trace should help users and operators understand:

- which tool was considered
- why it was selected or skipped
- whether a feature flag blocked it
- whether preview was generated
- whether confirmation is required
- whether a write was skipped or executed
- why an error or no-op happened

Trace and audit must not leak:

- tokens
- credentials
- prompt secrets
- full sensitive memory content
- full document content
- full email content
- external service secrets

For sensitive user data, logs should prefer ids, redacted snippets, counts,
categories, and user-visible summaries.

## 11. Error Contract Binding

Each registry entry should bind to an error contract version. Tool-specific
errors may exist later, but the shared error categories should remain stable.

Shared error categories:

- `validation_error`
- `unauthorized`
- `forbidden_cross_user`
- `feature_disabled`
- `confirmation_required`
- `pending_action_not_found`
- `pending_action_expired`
- `idempotency_conflict`
- `tool_unavailable`
- `external_side_effect_blocked`
- `write_failed`
- `partial_failure`

Type-specific handling:

- Read-only tools should prefer safe no-tool fallback when unavailable.
- Sensitive read tools must fail closed on auth or cross-user uncertainty.
- Preview tools should return no pending action when validation or guard checks
  fail.
- Confirm-required write tools must not retry writes without idempotency.
- External side-effect tools must fail closed when confirmation, auth, or
  release gate state is missing.
- Critical tools should emit audit for all blocked, skipped, failed, and
  completed attempts.

Registry entries should declare:

- `errorContractVersion`
- retry policy
- timeout policy
- user-visible error behavior
- audit-on-error behavior
- safe fallback behavior

## 12. Versioning and Compatibility

The registry needs stable versioning because pending actions may outlive a
single deploy or runtime revision.

Version fields:

- `toolVersion`
- `contractVersion`
- `inputSchemaRef`
- `outputSchemaRef`
- `previewSchemaRef`
- `confirmationSchemaRef`
- `errorContractVersion`

Backward-compatible changes:

- adding optional metadata fields
- adding new trace fields that consumers can ignore
- adding optional output fields
- tightening descriptions without changing behavior
- adding a new disabled-by-default tool entry

Breaking changes:

- renaming `toolName`
- changing required input fields
- changing preview payload semantics
- changing confirmation payload semantics
- changing idempotency semantics
- changing write behavior
- changing risk level to a lower-risk category without review
- removing error categories used by clients or pending actions

Pending action compatibility rules:

- Pending actions should store `toolName`, `toolVersion`, schema references, and
  action type at creation time.
- Confirm must use the pending action's stored version, not whatever registry
  version is current at confirm time.
- If preview schema and confirm schema become incompatible, confirm must fail
  safely and ask the user to regenerate preview.
- Deprecated tools may confirm existing pending actions only if their stored
  contract remains supported and safe.
- Migrating pending actions requires explicit migration design and audit.

Deprecated tool policy:

- Mark deprecated before removal.
- Provide `replacementToolName` when possible.
- Keep no-write fallback available.
- Do not silently map a pending action to a different mutating tool.

## 13. Relationship with Existing Phase 6 Tools

Phase 6 Memory Engine work maps into the Phase 7 registry as candidate tools:

- Memory retrieval is `read_only_retrieval`.
- Memory context injection is read-only context, not instruction authority.
- Memory proposal is `preview_proposal`.
- `save_memory_preview` remains proposal-only.
- Memory durable write is `confirm_required_write` and remains disabled.
- Durable memory write still requires a separate Release Gate.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- Timeline / summary extraction may later assist read-only or preview flows, but
  it must not write memory or `life_events` by itself.
- `life_events` must not be written by memory proposal runtime.

Existing Phase 5 `create_life_event` work maps separately:

- Life event proposal is preview-producing.
- Confirmed life event write is confirm-required and release-gated for
  production real write enablement.

Phase 7.1 does not change any Phase 6 or Phase 5 runtime behavior.

## 14. Proposed Follow-up Phases

Suggested future work:

- Phase 7.2 Read-only Runtime Tool Adapter Design
- Phase 7.3 Runtime Trace / Audit Contract
- Phase 7.4 Preview Tool Adapter Skeleton Design
- Phase 7.5 Confirm-required Tool Contract Design
- Phase 7.6 External Tool / MCP Boundary Design
- Phase 7.7 Phase 7 closeout

These are recommendations only. This document does not implement any follow-up
phase.

## 15. Verification Plan

Phase 7.1 verification is docs-only:

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

Recommended checks:

- `git diff --stat`
- `git diff -- docs/phase7_1_tool_registry_contract_design.md`
- `git status --short`
- `git diff --check`
- markdown lint only if an existing project command is available

## 16. Closeout Criteria

Phase 7.1 is complete when:

- `docs/phase7_1_tool_registry_contract_design.md` exists.
- The document is consistent with Phase 7.0 architecture.
- Registry metadata is clear.
- Risk classification is clear.
- Preview / confirm / write matrix is clear.
- Feature flag binding is clear.
- Auth and `userId` trust boundary are explicit.
- Trace / audit requirements are defined.
- Error contract binding is defined.
- Versioning and compatibility rules are defined.
- Follow-up phase recommendations are documented.
- Local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.1 defines the Tool Registry contract for future
multi-tool Agent runtime work. It does not implement registry code, enable tools,
connect durable memory write, modify production configuration, deploy, or
perform any user data writes.

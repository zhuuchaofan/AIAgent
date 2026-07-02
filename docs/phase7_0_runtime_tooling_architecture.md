# Phase 7.0 Runtime Tooling Architecture

Date: 2026-07-02

## 1. Background

LifeOS / LifeAgent has moved through the foundation phases that make a safe
multi-tool Agent runtime possible:

- Phase 1 established the authenticated application, API, Firestore-backed user
  data boundaries, and Cloud Run deployment shape.
- Phase 2 added life data, reminders, and daily summaries under user-scoped
  data paths.
- Phase 3 added document upload, processing, vector retrieval, and RAG document
  QA.
- Phase 4 introduced controlled Agent preview behavior, pending actions, and
  confirmation lifecycle concepts.
- Phase 5 completed the first Agent write MVP around `create_life_event`, while
  keeping real production writes behind feature gates and Release Gate review.
- Phase 6 completed the Memory Engine foundation: memory schema, proposal
  contract, retrieval skeleton, merge / conflict / pollution guard,
  timeline / summary extraction skeleton, read-only runtime context, and guarded
  memory proposal preview runtime.

Phase 6 is closed for preview-only / default-off implementation. Durable memory
write is not enabled, real Firestore Memory runtime is not connected, and
`users/{userId}/memories` is not being written. Memory proposal runtime must not
write `life_events`.

Phase 7 starts from the Phase 6 handoff recommendation: continue with runtime
integration and multi-tool expansion planning rather than opening durable memory
write. Phase 7.0 therefore defines the architecture for future multi-tool
runtime behavior. It does not release real writes.

The recent dedicated smoke blocker has also been clarified: the final cause of
the 401 was an incomplete `FIREBASE_ID_TOKEN` copy, not an API revision mismatch
or auth code inconsistency. That correction should prevent future Phase 7 work
from chasing the wrong auth conclusion.

## 2. Phase 7 Goal

Phase 7 evolves the Agent from a narrow RAG / memory proposal surface into a
safe multi-tool runtime.

Phase 7 should support:

- read-only tools
- preview-producing tools
- confirm-required write tools
- a unified tool invocation contract
- shared trace, audit, validation, and safety guard semantics
- default-off feature flags
- Release Gate separation for production-impacting writes

The architectural goal is not "more autonomy"; it is controlled tool expansion.
Every tool must be classified, gated, observable, and reversible according to
its risk.

## 3. Non-goals

Phase 7.0 does not:

- implement runtime code
- modify API runtime behavior
- modify frontend behavior
- enable durable memory write
- connect real Firestore Memory runtime
- write `life_events`
- write `users/{userId}/memories`
- change Firestore Rules
- change Cloud Run environment variables
- deploy
- enable MCP
- enable agent write flags
- enable memory proposal flags
- enable mock auth or mock LLM
- enable real external side-effect tools

Any durable write, production memory repository rollout, Cloud Run env change,
Firestore Rules change, MCP enablement, deployment, or production data mutation
must remain separately approved and separately verified.

## 4. Tool Categories

### Read-only Tools

Examples:

- RAG retrieval
- memory retrieval
- timeline retrieval
- daily summary retrieval
- document search
- status / diagnostics

Default behavior:

- May execute after auth, eligibility, and feature gate checks.
- Must emit trace.
- Must not write user data.
- Must not create pending actions.
- Must not trigger extraction or proposal generation as a hidden side effect.

Risk level: low to medium, depending on sensitivity of retrieved data and
prompt exposure. Read-only does not mean privacy-free.

### Preview-producing Tools

Examples:

- life event proposal
- memory proposal
- task proposal
- note proposal
- future calendar event preview
- future document update preview

Default behavior:

- May produce a pending proposal.
- Must be user-visible.
- Must remain no-write until confirmation and write gates allow otherwise.
- Must validate payloads server-side.
- Must include risk and guard results when applicable.

Risk level: medium. These tools can shape user decisions and create pending
actions, but they must not durably mutate user data by themselves.

### Confirm-required Write Tools

Examples:

- confirmed `life_event` write
- durable memory write
- future task / reminder write
- future calendar create / update / delete
- future document write / update

Default behavior:

- Must require explicit user confirmation.
- Must reference a server-side pending action.
- Must use idempotency.
- Must emit audit.
- Must remain default-off.
- Must pass Release Gate before production durable writes.

Risk level: high. These tools can mutate durable user data and must never run
directly from LLM output or Planner selection alone.

### External Side-effect Tools

Examples:

- calendar
- email
- file / Drive
- MCP tools
- third-party APIs

Default behavior:

- Read-only external tools require explicit capability design, auth scoping, and
  trace.
- Mutating external tools require preview, confirmation, audit, idempotency,
  rollback or compensation strategy, and separate Release Gate approval.
- MCP tools are out of scope for normal Phase 7 implementation unless a
  dedicated MCP boundary phase is approved.

Risk level: medium to critical, depending on whether the tool can notify others,
modify external systems, delete files, or expose private content.

## 5. Runtime Contract

Future tool invocation should use a uniform contract. A tool call record should
contain at least:

- `toolName`
- `toolVersion`
- `actionType`
- `userId`, resolved from auth context and never from frontend payload
- `input`
- `preview`
- `confirmationRequired`
- `idempotencyKey`
- `riskLevel`
- `writesData`
- `sideEffects`
- `traceId`
- `correlationId`
- `result`
- `error`

The runtime contract must preserve the userId trust boundary. Frontend payloads,
LLM output, tool input, query strings, and request bodies are not trusted sources
for protected user identity. Server-side auth context is the only source for
user-scoped reads and writes.

Tool input should describe requested behavior, not authority. The runtime owns
authorization, feature gate checks, risk classification, pending action lookup,
and write eligibility.

## 6. Preview / Confirm / Write Strategy

Read-only tools may execute directly after eligibility checks, but they must
produce trace and must not write.

Preview-producing tools may create pending proposals, but they cannot perform
durable mutations. Their output should be serialized in a stable preview
contract that can be shown to the user and later confirmed by reference.

Confirm-required write tools must execute only after the user confirms a
server-side pending action. Confirm must not trust a frontend-resubmitted
payload. The confirm request should reference the pending action id; the server
then loads the action, validates ownership, checks status and expiration,
revalidates gates, applies idempotency, and only then calls a write coordinator
when a Release Gate has allowed that write path.

Write paths must remain default-off. A disabled write gate should produce a
safe no-write response such as `previewOnly=true`, `wroteData=false`, and a
clear user-visible explanation.

Release Gates remain independent from development phases. Phase 7 development
can design and build default-off contracts, but it must not silently enable
production durable writes.

## 7. Tool Registry Design

The Tool Registry is a declarative capability catalog. It does not enable a
tool by itself.

A registry entry should include:

- tool metadata
- tool name and version
- capability declaration
- category
- risk classification
- input schema
- output schema
- feature flag binding
- preview / confirm / write support matrix
- auth requirements
- audit requirements
- owner service
- allowed side effects
- blocked side effects

Registry boundaries:

- Registry presence does not mean runtime availability.
- Feature flags determine whether a tool may be selected or executed.
- Tool category determines the required execution path.
- Durable write capability does not mean durable write is enabled.
- Planner cannot use registry metadata to bypass confirmation, auth, or feature
  gates.

Phase 7.0 only designs this concept. It does not implement a registry.

## 8. Execution Trace / Audit

Future runtime should emit trace and audit records that explain what happened
without exposing sensitive internals.

Trace / audit dimensions:

- request trace
- tool selection trace
- preview trace
- confirm trace
- write trace
- no-write reason
- skipped reason
- error reason
- feature gate state
- risk level
- user-visible explanation
- correlation id across request, pending action, and result

Trace restrictions:

- Do not log tokens.
- Do not log credentials.
- Do not expose prompt secrets.
- Do not log full sensitive memory, document, email, or external content by
  default.
- Prefer ids, summaries, categories, and redacted snippets for diagnostics.

Trace should help the user understand Agent behavior: which tool was considered,
why it was selected or skipped, whether a preview was generated, whether
confirmation is required, and whether a write was skipped or executed.

## 9. Error Handling

The runtime should use structured error categories:

| Error | Retry | User-visible | Audit |
| --- | --- | --- | --- |
| `validation_error` | Usually no, after user edits | Yes | Optional |
| `unauthorized` | After re-auth | Yes | Yes |
| `forbidden_cross_user` | No | Yes, generic | Yes |
| `feature_disabled` | No | Yes | Yes |
| `preview_only` | No | Yes | Yes |
| `confirmation_required` | After user confirms | Yes | Optional |
| `pending_action_not_found` | No | Yes | Yes |
| `pending_action_expired` | User can regenerate preview | Yes | Yes |
| `idempotency_conflict` | No automatic retry | Yes | Yes |
| `tool_unavailable` | Maybe | Yes | Yes |
| `external_side_effect_blocked` | No | Yes | Yes |
| `write_failed` | Only through idempotent coordinator | Yes | Yes |
| `partial_failure` | Case-specific compensation | Yes | Yes |

Error handling rules:

- Fail closed for mutating and external side-effect tools.
- Prefer no-op or read-only fallback over unsafe escalation.
- Never convert a failed preview into a write.
- Never retry a durable write without idempotency.
- Expose a clear user-facing explanation without leaking internals.
- Emit diagnostics sufficient for future debugging and audit.

## 10. Safety Boundaries

Phase 7 runtime design must preserve these boundaries:

- Frontend-provided `userId` is not trusted.
- User data access requires authenticated server-side identity.
- Cross-user access is forbidden.
- New capabilities remain default-off.
- No implicit write.
- No silent durable memory write.
- No `life_events` write from memory proposal runtime.
- No external side effect without confirmation.
- No MCP enablement in a normal implementation phase.
- No deployment mixed with docs or code phases.
- No Firestore Rules change without explicit approval.
- No Cloud Run env change without explicit approval.
- No production Firestore or Cloud Storage write without explicit approval.
- No mock auth or mock LLM in production.
- No LLM output directly writes Firestore data.

The runtime Agent must not mutate deployment configuration, Cloud Run env,
Firestore Rules, MCP configuration, or production infrastructure.

## 11. Relationship with Phase 6 Memory Engine

Phase 6 Memory Engine outputs become candidate Phase 7 tools:

- Memory retrieval is a read-only tool candidate.
- Memory context injection is a read-only runtime context candidate, not an
  instruction source.
- Memory proposal is a preview-producing tool candidate.
- `save_memory_preview` remains proposal-only.
- Durable memory write is a confirm-required write tool candidate and still
  requires a separate Release Gate.
- Real Firestore Memory runtime remains disconnected.
- `users/{userId}/memories` remains unwritten.
- Memory proposal runtime must not write `life_events`.

Phase 7 should treat Phase 6 memory work as a controlled tool subset rather than
as permission to enable durable memory persistence.

## 12. Proposed Phase 7 Breakdown

Suggested future phases:

- Phase 7.1 Tool Registry Contract Design
- Phase 7.2 Read-only Runtime Tool Adapter Design
- Phase 7.3 Runtime Trace / Audit Contract
- Phase 7.4 Preview Tool Adapter Skeleton
- Phase 7.5 Confirm-required Tool Contract
- Phase 7.6 External Tool / MCP Boundary Design
- Phase 7.7 Phase 7 closeout

These are planning recommendations only. This document does not implement any
of them.

## 13. Verification Plan

Phase 7.0 verification is docs-only:

- confirm docs-only diff
- confirm no runtime code changed
- confirm no frontend changed
- confirm no Cloud Run env changed
- confirm no Firestore Rules changed
- confirm no MCP changed
- confirm no deployment occurred
- confirm no feature flags were enabled
- confirm no durable writes were enabled
- confirm no `users/{userId}/memories` writes occurred
- confirm no `life_events` writes occurred from memory proposal runtime

Recommended local checks:

- `git diff --stat`
- `git diff -- docs/phase7_0_runtime_tooling_architecture.md`
- `git status --short`
- markdown lint only if an existing project command is available

## 14. Closeout Criteria

Phase 7.0 is complete when:

- `docs/phase7_0_runtime_tooling_architecture.md` exists.
- The document is consistent with Phase 6 closeout and handoff.
- Durable memory write remains explicitly out of scope.
- Real Firestore Memory runtime remains disconnected.
- Runtime tool categories and safety boundaries are defined.
- Preview / confirm / write strategy is defined.
- Tool registry, trace / audit, and error handling are designed at architecture
  level.
- Future Phase 7 breakdown is documented.
- Local docs-only commit is created.
- No push is performed.

Final conclusion: Phase 7.0 establishes the architecture for safe multi-tool
Agent expansion. It does not enable durable writes, production memory
persistence, external side-effect tools, deployment changes, or autonomous
mutation. All mutating capabilities remain behind preview / confirm, feature
flags, audit, idempotency, and future Release Gates.

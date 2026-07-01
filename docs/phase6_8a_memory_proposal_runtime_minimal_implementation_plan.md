# Phase 6.8A: Memory Proposal Runtime Minimal Implementation Plan

Date: 2026-07-01

## Scope

Phase 6.8A defines a minimal implementation plan for future Memory Proposal
Runtime Integration. It does not implement the plan.

This phase is planning-only:

- No code changes.
- No deployment.
- No real Firestore connection.
- No production API endpoint.
- No `users/{userId}/memories` writes.
- No `life_events` writes.
- No durable memory write enablement.
- No Cloud Run environment changes.
- No Firestore Rules changes.
- No MCP changes.
- No Phase 6.8A implementation.

The output is a bounded execution plan for a later, separately approved
implementation stage.

## Current State

Current main phase: Phase 6 Memory Engine.

Recent completed work:

- Phase 6.7A read-only `MemoryContextProvider` implementation is complete.
- Phase 6.7A local verification passed.
- Phase 6.7A preview-only API deployment smoke passed.
- API revision: `life-agent-api-00038-w9d`.
- `smoke-agent-life-event-write`: PASS.
- `smoke-rag-e2e`: PASS.

Current runtime boundaries:

- Memory retrieval flags are default-off / not set.
- Durable memory write is not enabled.
- Real Firestore Memory runtime is not connected.
- `users/{userId}/memories` is not written.
- Retrieval does not create pending actions.
- Extraction is not triggered.
- `save_memory_preview` is not automatically created by retrieval.
- Cloud Run env, Firestore Rules, and MCP were not modified.

Already completed design:

- Phase 6.8 Memory Proposal Runtime Integration Design is complete.

Not started:

- Phase 6.8A implementation.
- Durable Memory write Release Gate.
- Memory Dashboard / Forget / Audit.

## Implementation Objective

The future implementation objective is to build the smallest safe Memory
Proposal Runtime Integration.

The first version may allow:

- Explicit memory intent to generate a `save_memory_preview` proposal.
- `MemoryProposalGuard` to run before proposal creation.
- `blocked`, `review_required`, or `allowed` decisions to be represented in a
  pending action payload.
- Sensitive proposals to be blocked.
- Conflict or merge-candidate proposals to require review.
- Confirm flow to remain preview-only.
- Confirm flow to keep `wroteData=false`.
- No durable memory write.

The first version must not allow:

- Automatic extraction.
- Background memory proposal generation.
- RAG/chat automatic memory proposal generation.
- Durable memory write.
- Firestore Memory repository.
- Memory Dashboard implementation.
- Forget / Audit implementation.
- Any proposal path that bypasses preview plus confirm.

## Proposed Minimal Runtime Shape

The future implementation should introduce a narrow orchestration layer for
proposal creation rather than spreading Memory logic through Agent runtime.

Candidate shape:

- Add a `MemoryProposalService` or equivalent orchestration layer.
- Reuse `MemoryPreviewActionPayload`.
- Reuse `MemoryPreviewActionPayloadMapper`.
- Reuse `MemoryValidator`.
- Invoke `MemoryProposalGuard` before pending action creation.
- Add safe guard decision metadata to the pending action payload.
- Include conflict and merge-candidate metadata when relevant.
- Keep confirm flow preview-only and non-durable.

### `AgentActionExecutor.cs`

Possible future change:

- Before creating a `save_memory_preview` pending action, call the proposal
  orchestration layer.
- Convert guard decisions into a visible pending action shape.
- Reject blocked proposals without creating a silent durable side effect.

Boundary:

- Do not create durable memory.
- Do not write `users/{userId}/memories`.
- Do not write `life_events`.
- Do not trigger extraction.
- Do not create hidden memory proposals.

Risk:

- If guard behavior is implemented as logging only, unsafe proposals could still
  reach pending action state.

### `AgentContractValidator.cs`

Possible future change:

- Validate the extended `save_memory_preview` payload shape.
- Enforce allowed guard decision values.
- Reject malformed or contradictory guard metadata.

Boundary:

- Keep existing `save_memory_preview` contract compatible when flags are off.
- Do not validate or permit durable write payloads.

Risk:

- Overly broad schema changes could alter existing preview-only behavior.

### Pending Action Payload Schema

Possible future additions:

- `guardDecision`.
- `guardReason`.
- `conflictCandidates`.
- `mergeCandidates`.
- `reviewRequired`.

Boundary:

- Metadata must be user-visible and safe.
- Metadata must not include sensitive raw memory content beyond the visible
  proposal the user is already reviewing.
- Existing pending action behavior must remain unchanged when feature flags are
  off.

Risk:

- Adding metadata without tests can accidentally change confirm semantics.

### Avoiding AgentRunner Branch Growth

Required approach:

- Keep proposal orchestration outside `AgentRunner`.
- Let intent/contract flow identify the action type.
- Let executor/proposal service handle guard decisions.
- Keep feature flag checks centralized.
- Do not add memory-specific routing branches to `AgentRunner`.

### Preview Plus Confirm Guarantee

Every future implementation path must satisfy:

- Proposal is visible to the user.
- Guard has run before pending action creation.
- Confirm remains preview-only.
- Confirm returns `wroteData=false`.
- Durable write is impossible until a separate Release Gate.

## Feature Flags

Future implementation should use default-off flags.

Required flags:

- `ENABLE_MEMORY_PROPOSAL_GUARD`
- `ENABLE_MEMORY_PROPOSAL_RUNTIME`

Optional controls:

- Test user allowlist.
- Explicit memory intent allowlist.
- Environment-specific rollout allowlist.

Rules:

- All flags default off.
- Flag off keeps current `save_memory_preview` behavior unchanged.
- Flag on may create only preview proposals.
- Flag on must not create durable memory.
- Flag on must not trigger automatic extraction.
- Flag on must not use a real Firestore Memory repository.

## Files Expected To Change In Future Implementation

This section lists likely future files. This phase does not modify them.

### `LifeAgent.Api/Services/Agent/AgentActionExecutor.cs`

Why it may change:

- It currently creates Agent action payloads and pending action behavior.
- It is the likely boundary for invoking proposal guard orchestration before
  `save_memory_preview` pending action creation.

Modification boundary:

- Add guarded proposal handling only for explicit memory proposal paths.
- Preserve existing preview-only confirm semantics.

Risk:

- Pending action creation can become unsafe if guard decisions are ignored.

### `LifeAgent.Api/Services/Agent/AgentContractValidator.cs`

Why it may change:

- It may need to validate guard metadata and extended preview payload shape.

Modification boundary:

- Validate only preview proposal metadata.
- Do not introduce durable write contract.

Risk:

- Contract changes can regress existing Agent action tests.

### `LifeAgent.Api/Models/Memories/MemoryPreviewActionPayload.cs`

Why it may change:

- It may need explicit guard decision, conflict, or merge-candidate metadata.

Modification boundary:

- Keep the payload user-visible and preview-only.
- Avoid sensitive hidden fields.

Risk:

- Payload expansion can leak sensitive context if not carefully redacted.

### `LifeAgent.Api/Services/Memories/MemoryPreviewActionPayloadMapper.cs`

Why it may change:

- It may need to map guard decisions into pending action payload fields.

Modification boundary:

- Map only safe metadata.
- Do not map durable write instructions.

Risk:

- Mapper behavior can silently normalize blocked proposals into allowed ones.

### `LifeAgent.Api/Services/Memories/MemoryProposalGuard.cs`

Why it may change:

- It may need runtime-safe decision output or adapter behavior.

Modification boundary:

- Guard decisions must be enforceable, not logging-only.
- Sensitive proposals must block.

Risk:

- A warning-only guard would not protect runtime proposal flow.

### `LifeAgent.Api/Program.cs`

Why it may change:

- Future implementation may register a proposal orchestration service and bind
  feature flags.

Modification boundary:

- No real Firestore Memory repository.
- No durable write services.
- Default flags remain off.

Risk:

- DI registration could accidentally enable runtime behavior without flags.

### Tests

Likely future test areas:

- Pending action tests.
- `AgentSkeletonTest.cs`.
- New Memory proposal runtime tests.
- Contract validator tests.
- Mapper tests.
- Smoke scripts if needed.

Modification boundary:

- Tests must prove no-write and preview-only semantics.

Risk:

- Without regression tests, proposal flow can accidentally create durable write
  behavior.

### Docs

Why they may change:

- Future implementation result, verification, and smoke result documents should
  record exact behavior and boundaries.

Modification boundary:

- Documentation must not claim durable Memory is live.

Risk:

- Ambiguous docs can make preview-only behavior look like production Memory
  enablement.

## Tests Required Before Implementation Completion

Future implementation must not be considered complete until tests cover:

- Explicit memory intent still creates `save_memory_preview`.
- Guard allowed proposal creates a pending action.
- Sensitive proposal is blocked.
- Conflict proposal is marked `review_required`.
- Merge candidate is recorded but not auto-merged.
- Confirm `save_memory_preview` remains `previewOnly=true`.
- Confirm `save_memory_preview` remains `wroteData=false`.
- No `users/{userId}/memories` write.
- No `life_events` write.
- No durable memory write.
- No extraction trigger.
- No RAG regression.
- No life_event regression.
- No read-only retrieval regression.
- Authenticated smoke plan update.

## No-write Smoke Design

Future smoke should prove:

- `save_memory_preview` proposal is created only when expected.
- Guard blocks sensitive proposal.
- Confirm remains preview-only.
- `wroteData=false`.
- No `users/{userId}/memories` write.
- No `life_events` write.
- No durable memory write.
- Memory retrieval remains default-off unless explicitly enabled.
- Existing `smoke-agent-life-event-write` still PASS.
- Existing `smoke-rag-e2e` still PASS.

Smoke must not:

- Enable real write flags.
- Enable durable Memory write.
- Modify Cloud Run env without explicit approval.
- Mutate Firestore memory collections.

## Rollback Plan

Future implementation should support:

- Feature flag off rollback.
- Proposal guard disabled fallback.
- Malformed guard decision rejection.
- Pending action schema fallback.
- Smoke failure handling.
- Deployment rollback if a future deployment fails.

Rollback expectations:

- Turning off `ENABLE_MEMORY_PROPOSAL_RUNTIME` must restore current
  `save_memory_preview` behavior.
- Turning off `ENABLE_MEMORY_PROPOSAL_GUARD` must not enable unsafe durable
  writes.
- Malformed guard decisions must fail closed.
- Failed smoke must block rollout and require investigation before retry.

## Implementation Stop Conditions

Future implementation must stop if it requires:

- Durable memory write.
- Firestore Memory repository.
- `users/{userId}/memories` write.
- Background extraction.
- RAG/chat automatic proposal generation.
- Cloud Run write flag change.
- Firestore Rules change.
- Confirm `save_memory_preview` returning `wroteData=true`.
- Pending action creation without a user-visible proposal.
- Guard behavior that only logs warnings without affecting runtime behavior.
- Proceeding after no-write smoke fails.
- Proceeding after RAG or life_event regression fails.

## Anti-Patterns

Do not implement:

- LLM-decided durable memory write.
- Guard-only logging with no enforcement.
- Mixed `save_memory_preview` and `save_memory` semantics.
- Confirm directly writing memory.
- Extraction automatically writing active memory.
- Silent memory proposal creation.
- User-invisible memory proposal creation.
- Durable write before Memory Dashboard / Forget / Audit exists.
- Memory-specific if/else routing inside `AgentRunner`.

## Final Decision

Phase 6.8A currently completes only an implementation plan.

Current decision:

- Do not start Phase 6.8A implementation.
- Do not deploy.
- Do not connect real Firestore.
- Do not enable durable memory write.
- Do not create production Memory API endpoints.

If the project continues into Phase 6.8A implementation, it must be separately
approved by the user. The first implementation may only create guarded,
user-visible `save_memory_preview` proposals. Durable Memory write remains
reserved for a separate Release Gate.

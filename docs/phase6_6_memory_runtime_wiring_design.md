# Phase 6.6: Memory Runtime Wiring Design

Date: 2026-07-01

## Scope

Phase 6.6 is a runtime wiring design phase only. It describes how the existing
Memory skeleton could be connected to runtime in future phases without changing
runtime behavior now.

This document does not implement code, connect runtime services, add production
API endpoints, register repositories, write Firestore data, change Cloud Run
configuration, change Firestore Rules, modify MCP, deploy, or enable durable
memory writes.

## Project Phase Sync

Current main phase: Phase 6 Memory Engine.

Current precise state: Phase 6.5 completed / integration readiness checkpoint.

Completed Phase 6 work:

- Phase 6.0 Memory Engine Architecture Design: complete.
- Phase 6.1 Memory Taxonomy & Schema: complete.
- Phase 6.2 Memory Proposal & Confirm Contract: complete.
- Phase 6.3 Memory Retrieval Skeleton: complete.
- Phase 6.4 Merge / Conflict / Pollution Guard: complete.
- Phase 6.5 Timeline / Summary Extraction Skeleton: complete.
- Phase 6.1 to 6.5 closeout: complete.
- Post-closeout authenticated smoke: complete.
- Memory Integration Readiness Plan: complete.

Do not interpret this as:

- Phase 6 fully complete.
- Memory Engine online.
- Memory runtime connected.
- Durable memory write allowed.
- Firestore memory repository ready for production.

`docs/skills` remains governance documentation. It is not a runtime policy engine
and does not enforce Agent, Planner, Firestore, or Cloud Run behavior.

## Current Baseline

Already completed:

- Phase 6.1 Memory schema, validator, and fake repository.
- Phase 6.2 `save_memory_preview` contract.
- Phase 6.3 fake retrieval skeleton.
- Phase 6.4 merge / conflict / pollution guard.
- Phase 6.5 timeline / summary extraction skeleton.
- Closeout review.
- Post-closeout authenticated smoke.
- Integration readiness plan.

Current runtime boundary:

- Memory runtime is not connected.
- Durable memory write is not enabled.
- No real Firestore memory repository is connected.
- `AgentRunner` / Planner do not call memory retrieval, extraction, or guard.
- No memory is injected into prompts.
- No production memory API endpoint exists.
- Memory remains local-only / fake-only / preview-only.

Current smoke baseline:

- `smoke-agent-life-event-write`: PASS.
- `smoke-rag-e2e`: PASS.
- Real writes remain disabled.
- `RUN_AGENT_WRITE_SMOKE`, `EXPECT_AGENT_WRITE_ENABLED`, and
  `RUN_MUTATING_SMOKE` remain unset for the authenticated preview-only smoke.

## Runtime Wiring Goals

Future runtime wiring should be designed around these goals:

- Define how read-only Memory retrieval can enter Agent flow without silently
  changing Planner behavior.
- Define where `MemoryProposalGuard` runs before a memory proposal reaches the
  user and before any future durable write.
- Define how `MemoryExtractionService` can be triggered in a controlled way.
- Keep `save_memory_preview` bound to preview plus confirm.
- Keep durable memory write outside normal development and behind a separate
  Release Gate.
- Preserve Execution Contract Engine v1 by adding explicit contracts rather than
  scattering memory-specific branches across runtime code.
- Make no-write behavior testable before any read or proposal integration.

Non-goals for Phase 6.6:

- No code.
- No runtime integration implementation.
- No durable memory write.
- No real Firestore repository.
- No AgentRunner or Planner connection.
- No production API endpoint.
- No deployment.

## Candidate Wiring Points

### A. AgentRunner Pre-retrieval

Current status:

- `AgentRunner` is active for existing Agent Preview behavior.
- Memory retrieval is fake/local only.
- No memory retrieval is called by `AgentRunner`.
- No memory context reaches Planner prompts.

Future possibility:

- Add a read-only retrieval step before planning.
- Route results through a retrieval gate that enforces user scope, memory type,
  status, expiration, redaction, relevance, and token budget.
- Include memory context as a clearly labeled, non-instructional context block.

Risks:

- Irrelevant memory can pollute planning.
- Sensitive memory can leak into LLM prompts.
- Retrieved memory can be treated as instruction rather than context.
- Token budget can crowd out RAG evidence or user intent.
- Invisible personalization can surprise users.

Recommended now:

- Do not connect now.
- Design only. Phase 6.7 would need separate approval before read-only
  integration or simulation.

### B. AgentIntentResolver / BuildPlan

Current status:

- Intent resolution and plan building are part of the active Agent runtime.
- Memory skeleton is not part of intent resolution or planning.
- Execution Contract Engine v1 should remain the organizing boundary.

Future possibility:

- Use read-only memory context as an input to planning after retrieval gates.
- Allow memory-aware planning only through explicit contract fields, not hidden
  branches.
- Keep intent resolution focused on user intent and contract selection.

Risks:

- Memory-specific branches can flow back into `AgentRunner` or intent resolver
  and recreate large if/else behavior.
- Planner behavior can become hard to audit if memory context is implicit.
- Memory can bias action selection without user-visible controls.
- Execution Contract Engine v1 can be weakened if memory bypasses contract
  validation.

Recommended now:

- Do not connect now.
- Design contract surfaces first. Avoid memory-aware planning until retrieval
  input shape, audit fields, and no-memory fallback behavior are specified.

### C. AgentActionExecutor

Current status:

- Existing code can produce `save_memory_preview` semantics.
- `save_memory_preview` remains preview-only.
- Durable memory write is not available.

Future possibility:

- Let `AgentActionExecutor` construct memory proposal actions after a guarded
  proposal decision.
- Run `MemoryProposalGuard` before emitting a proposal, or require a guard result
  as an input to action construction.
- Keep action output aligned with Phase 6.2 payload validation.

Risks:

- Proposal noise if runtime generation is too eager.
- Guard bypass if executor constructs memory proposals directly from raw input.
- Confusion between `save_memory_preview` and future durable save behavior.
- Runtime code may accidentally evolve from preview to write semantics.

Recommended now:

- Do not expand runtime behavior now.
- Design executor input/output contracts and guard requirements before any
  implementation.
- Avoid durable write behavior in `AgentActionExecutor`.

### D. Confirm Flow

Current status:

- Confirm flow is active for pending Agent actions.
- `save_memory_preview` confirm is preview-only.
- Confirm returns no durable memory resource.

Future possibility:

- A later Release Gate could allow confirmed memory proposals to create or update
  durable memory records.
- Confirm write path would require idempotency keys, audit events, rollback
  metadata, user-scoped repository writes, Firestore Rules validation, and smoke
  coverage.

Required future gates:

- Explicit durable memory write approval.
- Dedicated canary.
- Rollback plan.
- Hard-delete and forget plan.
- Firestore Rules review.
- Authenticated write smoke.
- No-write fallback smoke.

Risks:

- Duplicate writes if idempotency is weak.
- User isolation failures if repository scope is wrong.
- Missing audit or hard-delete semantics can make memory difficult to govern.
- Confirm can become a backdoor for durable writes if preview semantics are not
  kept explicit.

Recommended now:

- Do not connect durable write now.
- Keep confirm preview-only for Memory.
- Treat durable memory writes as Release Gate-only.

### E. Background Extraction / Daily Summary

Current status:

- `MemoryExtractionService` is local-only.
- It is not connected to daily summary, background workers, Agent runtime, or API
  endpoints.
- It does not write active memory.

Future possibility:

- Trigger extraction from an explicit user request, daily summary completion, or
  a scheduled process only after design approval.
- Emit only `save_memory_preview` proposals.
- Apply proposal guard, deduplication, rate limiting, audit, and user-visible
  review surfaces.

Risks:

- Background extraction can create surprising or noisy proposals.
- Daily summary errors can become memory proposals.
- Temporary emotion or low-confidence inference can be mistaken for stable
  memory.
- Automatic writes would pollute durable memory if preview plus confirm is
  bypassed.

Recommended now:

- Do not connect now.
- Future extraction must generate proposals only, not active memory.

## Recommended Wiring Order

Runtime wiring must be phased. It should not be connected all at once.

### Step 1: Read-only Retrieval Design Only

Goal:

- Define retrieval contract, inputs, outputs, gates, no-memory fallback behavior,
  and audit fields.

Allowed:

- Documentation.
- Truth-map updates.
- Contract diagrams.
- Smoke design.

Forbidden:

- Code changes.
- AgentRunner connection.
- Planner prompt injection.
- Firestore repository.
- Durable writes.

Test requirements:

- Define no-write smoke expectations.
- Define retrieval-disabled smoke expectations.

Completion standard:

- Reviewable design exists and identifies all runtime boundaries.

User confirmation:

- Required before moving beyond design.

### Step 2: Read-only Retrieval Fake Runtime Simulation

Goal:

- Simulate read-only memory retrieval using fake/local memory without durable
  write or production repository behavior.

Allowed:

- Only after separate user approval.
- Fake/local data.
- Explicit feature gate or test-only path.
- No-write smoke.

Forbidden:

- Real Firestore memory repository.
- Prompt injection without visible boundary.
- Durable memory write.
- Production API endpoint.

Test requirements:

- Existing Agent smoke still passes when retrieval is disabled.
- Retrieval-enabled simulation performs no writes.
- Authenticated preview-only smoke still passes.

Completion standard:

- Runtime behavior is reversible and no-write proof is captured.

User confirmation:

- Required.

### Step 3: Guarded Memory Proposal Runtime Design

Goal:

- Design how memory proposals are generated and guarded before user display.

Allowed:

- Design guard placement.
- Define proposal audit fields.
- Define review-required behavior.

Forbidden:

- Runtime proposal generation without approval.
- Guard bypass.
- Durable memory write.
- Background extraction.

Test requirements:

- Sensitive content rejection / review-required cases.
- Duplicate/conflict proposal behavior.
- Confirm remains preview-only.

Completion standard:

- Proposal flow has clear guard, validation, audit, and no-write boundaries.

User confirmation:

- Required before implementation.

### Step 4: Extraction-to-preview Proposal Design

Goal:

- Design how timeline/summary extraction can emit `save_memory_preview`
  proposals.

Allowed:

- Design trigger rules.
- Design rate limits and deduplication.
- Design user review surfaces.

Forbidden:

- Automatic active memory creation.
- Direct `users/{userId}/memories` writes.
- Background job implementation.
- Daily summary runtime connection.

Test requirements:

- Extraction emits proposals only.
- Low-confidence and sensitive proposals are rejected or review-required.
- No `life_events` or memory store writes.

Completion standard:

- Extraction-to-preview is specified without durable mutation.

User confirmation:

- Required before implementation.

### Step 5: User-visible Memory Management Design

Goal:

- Define user-facing memory list, edit, archive, forget, hard-delete, and audit
  boundaries.

Allowed:

- Product and API design.
- Audit and hard-delete semantics.
- Privacy and redaction policy.

Forbidden:

- Production API implementation.
- Firestore Rules changes.
- Durable write enablement.
- Irreversible delete behavior.

Test requirements:

- Future tests must cover view, forget, hard-delete, audit, and user isolation.

Completion standard:

- Users have a defined way to inspect and control memory before durable write is
  enabled.

User confirmation:

- Required before implementation.

### Step 6: Release Gate for Durable Write

Goal:

- Enable durable memory write only after dedicated release approval.

Allowed:

- Canary.
- Rollback.
- Authenticated write smoke.
- Firestore Rules verification.
- Dedicated test user or constrained rollout.

Forbidden:

- Implicit enablement from development phases.
- Broad rollout without canary.
- Durable write without Memory Dashboard / Forget / Audit strategy.
- Cloud Run env changes without explicit approval.

Test requirements:

- No-write smoke still works.
- Durable write smoke is scoped and authenticated.
- Rollback is exercised or documented as executable.
- Audit and hard-delete behavior are verified.

Completion standard:

- Durable writes are proven scoped, reversible, audited, and explicitly approved.

User confirmation:

- Required. This is a Release Gate, not an automatic continuation of Phase 6.

## Anti-Patterns

The following are prohibited:

- Directly wiring `InMemoryMemoryRetrievalService` into `AgentRunner`.
- Directly creating `FirestoreMemoryRepository`.
- Directly writing `users/{userId}/memories`.
- Letting extraction automatically write active memory.
- Treating `docs/skills` as a runtime policy engine.
- Letting retrieval automatically inject prompt context without a user-visible
  boundary.
- Enabling durable memory write before Memory Dashboard / Forget / Audit design.
- Bypassing preview plus confirm.
- Reintroducing large memory-specific if/else branches inside `AgentRunner`.
- Adding production memory endpoints before user-visible memory management is
  designed.
- Modifying Cloud Run env, Firestore Rules, or MCP as part of Phase 6.6.
- Deploying as part of Phase 6.6.

## Required Gates Before Any Implementation

Before any runtime implementation begins, the project must complete:

- Architecture review.
- Runtime path truth map update.
- No-write smoke design.
- Authenticated preview-only smoke design.
- Privacy / sensitive data review.
- Rollback / audit design.
- Forget / hard-delete design.
- Firestore Rules design.
- Memory Dashboard design.
- Explicit user approval.

Before durable write specifically, the project must additionally complete:

- Release Gate approval.
- Canary plan.
- Rollback command plan.
- Hard-delete verification plan.
- Authenticated durable-write smoke plan.
- Post-write audit and cleanup plan.

## Final Decision

Phase 6.6 only allows Runtime Wiring Design.

Current work must not enter runtime integration implementation.

Current work must not enable durable memory write.

Current work must not add or register a Firestore memory repository.

Current work must not connect AgentRunner or Planner to memory retrieval,
extraction, guard, or prompt injection.

Memory remains local-only / fake-only / preview-only.

If the project continues after this design, the next step must be separately
confirmed by the user. Phase 6.7 must not start implicitly.

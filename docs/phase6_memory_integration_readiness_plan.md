# Phase 6 Memory Integration Readiness Plan

Date: 2026-07-01

## Scope

This document is a readiness plan for future Memory runtime integration. It is
planning only.

It does not implement code, change business logic, wire runtime services, add
API endpoints, change Cloud Run configuration, change Firestore Rules, deploy
Web/API services, or enable durable writes.

## Current Real System State

Current completed foundation:

- Phase 6.1 Memory taxonomy/schema is complete.
- Phase 6.2 `save_memory_preview` contract is complete.
- Phase 6.3 memory retrieval skeleton is complete.
- Phase 6.4 merge/conflict/pollution guard is complete.
- Phase 6.5 timeline/summary extraction skeleton is complete.
- Phase 6.1 to 6.5 closeout is complete.
- Post-closeout authenticated smoke has passed.
- `smoke-agent-life-event-write`: PASS.
- `smoke-rag-e2e`: PASS.

Runtime status:

| Surface | Current status | Notes |
| --- | --- | --- |
| Agent runtime | ACTIVE | Existing Agent Preview and confirmation flow are available. |
| `save_memory_preview` preview contract | ACTIVE | Contract exists and confirm remains preview-only. |
| Durable memory write | INACTIVE | No durable memory write path is enabled. |
| Memory retrieval | local-only skeleton | Fake/local retrieval only; no AgentRunner or Planner injection. |
| Memory guard | local-only skeleton | Returns decisions only; does not mutate memory. |
| Memory extraction | local-only skeleton | Produces preview proposals only; no background/runtime trigger. |
| Firestore memory repository | INACTIVE | No real `users/{userId}/memories` repository is connected. |
| Memory production API | INACTIVE | No memory-specific production endpoint exists. |
| Cloud Run write flags | disabled | Real writes remain gated and disabled. |

`docs/skills` is governance documentation. It defines project process and phase
assessment rules, but it is not a runtime policy engine and does not enforce
Agent, Planner, Firestore, or Cloud Run behavior by itself.

## Why Memory Runtime Should Still Not Be Connected

The Memory skeleton is complete enough for local modeling, validation, preview
contracts, retrieval tests, guard decisions, and extraction tests. It has not yet
gone through runtime integration design.

Do not connect it directly to runtime because:

- It cannot be directly connected to `AgentRunner` without defining when memory
  is retrieved, filtered, token-budgeted, and audited.
- It cannot be directly connected to Firestore without a repository contract,
  Security Rules review, migration/rollback plan, audit model, and hard-delete
  semantics.
- Extraction cannot automatically produce durable writes; extraction must only
  create reviewable `save_memory_preview` proposals until a separate durable
  memory release gate is approved.
- Retrieval cannot automatically inject memory into the Planner prompt until a
  read-only retrieval gate defines privacy, relevance, prompt-injection, and
  token-budget limits.
- No memory write may bypass preview plus confirm.
- `save_memory_preview` confirmation currently proves preview semantics only:
  `previewOnly=true`, `wroteData=false`, and no durable resource creation.

## Future Integration Prerequisites

Before any Memory runtime integration implementation starts, the following must
be true:

- Production write flags remain default-off.
- A Memory runtime contract is designed first.
- A read-only retrieval gate is defined.
- A memory write release gate is defined.
- Rollback, audit, and hard-delete strategies are defined.
- User-visible memory management boundaries are defined.
- Privacy and sensitive-data redaction strategy is defined.
- Authenticated smoke coverage is defined and runnable.
- No-write / preview-only smoke coverage is defined and runnable.
- Firestore repository behavior is specified before any real repository is
  registered.
- Firestore Rules impact is reviewed before any memory collection is used.
- Planner prompt injection format, token budget, and exclusion rules are
  reviewed before retrieval reaches Planner.
- `save_memory_preview` remains the only allowed runtime memory proposal shape
  until durable write enablement is separately approved.

## Recommended Future Phase Split

This plan does not start implementation. It only recommends a future sequence.

### Phase 6.6: Memory Runtime Wiring Design

Goal:

- Design runtime connection points.
- Define request/response contracts between Agent runtime and Memory services.
- Define telemetry, audit, rollback, and failure behavior.

Boundary:

- Design only.
- No code.
- No Firestore.
- No API endpoint.
- No deployment.
- No write flag.

### Phase 6.7: Read-only Memory Retrieval Integration

Goal:

- Integrate retrieval only through fake/local or explicitly gated read-only
  memory.
- Prove that retrieval can be disabled without changing Agent behavior.
- Define prompt-injection limits and redaction behavior.

Boundary:

- No durable write.
- No automatic extraction.
- No Firestore write.
- No memory mutation.
- No bypass of user confirmation.

### Phase 6.8: Memory Proposal Runtime Integration

Goal:

- Allow extraction to generate `save_memory_preview` proposed actions.
- Keep all memory proposals reviewable and confirm-gated.

Boundary:

- Extraction may produce preview proposals only.
- Confirm still does not create durable memory unless a later release gate
  approves durable writes.
- No automatic durable memory write.
- No background extraction job without separate approval.

### Phase 6.9: Memory Management / Forget / Audit Design

Goal:

- Design user-visible memory viewing, deletion, audit, and hard-delete behavior.
- Define how users inspect, correct, forget, archive, and audit memories.

Boundary:

- Design only unless separately approved.
- No direct production enablement.
- No irreversible delete operation without a tested and audited plan.

### Release Gate: Durable Memory Write Enablement

Goal:

- Enable durable memory writes only after explicit approval.

Requirements:

- Separate user approval.
- Canary plan.
- Rollback plan.
- Smoke plan.
- Firestore Rules confirmation.
- Dedicated test user or constrained rollout.
- Proof that writes remain user-scoped and auditable.

Boundary:

- Release Gate is not a normal development phase.
- It must not be entered implicitly after Phase 6.8 or Phase 6.9.

## Prohibitions

Do not do any of the following now:

- Do not connect real Firestore.
- Do not add a production memory API.
- Do not register `MemoryRepository` in `Program.cs`.
- Do not inject retrieval into `AgentRunner`.
- Do not inject retrieval into Planner prompts.
- Do not connect extraction to a background task.
- Do not enable durable memory write.
- Do not bypass preview plus confirm.
- Do not modify Cloud Run environment variables.
- Do not modify Firestore Rules.
- Do not modify MCP.
- Do not deploy.

## Runtime Integration Candidate Analysis

This section analyzes possible integration points. It does not approve or
implement them.

### AgentRunner Pre-retrieval

Current status:

- `AgentRunner` is active for existing Agent Preview behavior.
- Memory retrieval is local-only and not connected to `AgentRunner`.
- No memory is injected into Planner prompts.

Future possible integration:

- Add a read-only Memory retrieval step before planning.
- Pass retrieved memories through a redaction, relevance, type/status, and token
  budget gate.
- Include only approved read-only memory context in Planner input.

Risks:

- Irrelevant memories can pollute Planner behavior.
- Sensitive memories can leak into prompts.
- Prompt injection risk increases if memory content is treated as instruction.
- Token budget pressure can degrade RAG and Agent response quality.
- Silent personalization can be confusing unless user-visible boundaries exist.

Recommended now:

- Do not integrate now.
- First design Phase 6.6 runtime contract and Phase 6.7 read-only retrieval gate.

### AgentActionExecutor Generating `save_memory_preview`

Current status:

- `save_memory_preview` preview contract is active.
- `AgentActionExecutor` can construct preview semantics, but durable memory write
  remains inactive.
- Confirm for memory preview remains `previewOnly=true` and `wroteData=false`.

Future possible integration:

- Allow runtime extraction or intent handling to ask `AgentActionExecutor` for a
  `save_memory_preview` proposed action.
- Keep proposal payload validation aligned with Phase 6.1 taxonomy and Phase 6.4
  guard decisions.

Risks:

- If proposal generation is too broad, the UI can become noisy.
- Low-confidence or sensitive proposals can erode trust.
- A future developer may accidentally treat `save_memory_preview` as durable
  save unless the contract remains explicit.

Recommended now:

- Do not expand runtime generation now.
- Keep this as a Phase 6.8 candidate after runtime design and guard placement are
  finalized.

### Confirm Flow Durable Memory Write

Current status:

- Confirm flow is active for existing pending actions.
- Memory preview confirmation is explicitly preview-only.
- Durable memory write is inactive.

Future possible integration:

- After Release Gate approval, confirmed memory proposals could create or update
  durable memory records through an audited repository.
- The confirm response would need explicit durable write semantics, resource ids,
  idempotency, rollback records, and failure behavior.

Risks:

- Confirm is the highest-risk write boundary because it can create durable user
  memory.
- Idempotency bugs can duplicate or corrupt memories.
- Missing audit/hard-delete behavior can violate user expectations.
- Firestore Security Rules and repository bugs can break user isolation.

Recommended now:

- Do not connect durable writes now.
- Treat durable memory write as a separate Release Gate, not a normal phase step.

### MemoryProposalGuard Layer Placement

Current status:

- `MemoryProposalGuard` is local-only.
- It returns decisions and does not mutate memory or runtime state.

Future possible integration:

- Run the guard before a `save_memory_preview` proposed action is shown.
- Run it again before any future durable write if repository state may have
  changed between proposal and confirm.
- Keep guard output visible enough for audit and user review when it blocks or
  requires review.

Risks:

- Running the guard too early may miss conflicts with newer memories.
- Running it only at confirm time may show unsafe proposals to users.
- Over-aggressive guard rules can suppress useful memory proposals.
- Under-aggressive rules can permit sensitive or polluted memory.

Recommended now:

- Do not wire now.
- Design a two-pass guard strategy in Phase 6.6 before integration.

### MemoryExtractionService Trigger

Current status:

- `MemoryExtractionService` is local-only.
- It accepts local timeline/summary-like inputs and returns preview proposal
  results.
- It is not connected to Agent runtime, daily summary jobs, background workers,
  or API endpoints.

Future possible integration:

- Trigger extraction from explicit user action, daily summary completion, or a
  controlled Agent proposal flow.
- Emit only `save_memory_preview` proposals.
- Require rate limits, deduplication, audit records, and no-write smoke coverage.

Risks:

- Background extraction can generate surprising proposals.
- Automatic extraction can convert transient emotions into durable-looking
  memory proposals.
- Triggering from daily summaries can compound summary errors.
- High-volume extraction can create noise and cost.

Recommended now:

- Do not connect now.
- Prefer explicit or tightly gated triggers after Phase 6.8 design.

## Smoke and Verification Expectations

Before any future runtime implementation is considered complete, verification
must include:

- Authenticated smoke for Agent runtime with memory feature disabled.
- Authenticated no-write smoke proving preview-only semantics.
- Retrieval disabled smoke proving existing Agent behavior still works.
- Retrieval enabled read-only smoke proving no durable write occurs.
- Memory proposal smoke proving `save_memory_preview` requires confirmation.
- Negative smoke proving sensitive/polluted memory proposals are rejected or
  review-required.
- Release Gate-only durable write smoke after separate approval.

The current post-closeout smoke evidence already proves:

- `smoke-agent-life-event-write`: PASS.
- `smoke-rag-e2e`: PASS.
- Real writes remain disabled.
- Memory skeleton remains local-only / fake-only / preview-only.
- Production Memory runtime wiring is not enabled.

## Final Conclusion

The project can enter Memory integration planning.

The project should not enter Memory runtime integration implementation yet.

Durable memory write should not be enabled now.

Memory skeleton remains local-only / fake-only / preview-only.

The next appropriate action is a Phase 6.6 Memory Runtime Wiring Design document
or review. That action should remain design-only unless the user explicitly
approves implementation work in a later step.

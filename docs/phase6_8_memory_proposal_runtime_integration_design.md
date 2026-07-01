# Phase 6.8: Memory Proposal Runtime Integration Design

Date: 2026-07-01

## Scope

Phase 6.8 designs future Memory Proposal Runtime Integration. It focuses on how
Memory extraction, proposal guard decisions, and the `save_memory_preview`
contract could safely enter runtime proposal flow later.

This phase is design-only:

- No runtime wiring implementation.
- No business code changes.
- No durable memory write enablement.
- No production API endpoint.
- No real Firestore memory repository.
- No deployment.
- No Cloud Run environment changes.
- No Firestore Rules changes.
- No MCP changes.
- No `AgentRunner`, `AgentIntentResolver`, `AgentActionExecutor`,
  `AgentContractValidator`, or `Program.cs` changes.
- No service registration for `MemoryRepository`, `MemoryExtractionService`, or
  `MemoryProposalGuard`.

Memory remains local-only / fake-only / preview-only after this document.

## Project Phase Sync

Current main phase: Phase 6 Memory Engine.

Current sub-stage:

- Phase 6.7 Read-only Memory Retrieval Integration Design is complete and
  committed.

Already completed:

- Phase 6.0 Memory Engine Architecture Design.
- Phase 6.1 Memory Taxonomy & Schema.
- Phase 6.2 `save_memory_preview` Contract.
- Phase 6.3 Memory Retrieval Skeleton.
- Phase 6.4 Merge / Conflict / Pollution Guard.
- Phase 6.5 Timeline / Summary Extraction Skeleton.
- Phase 6.1 to 6.5 Closeout.
- Post-closeout authenticated smoke.
- Memory Integration Readiness Plan.
- Phase 6.6 Runtime Wiring Design.
- Phase 6.7 Read-only Memory Retrieval Integration Design.

Current real state remains:

- Memory skeleton completed.
- Memory runtime not connected.
- Durable memory write disabled.
- Firestore memory repository not implemented / not registered.
- `AgentRunner` / Planner not connected to Memory Retrieval.
- Memory extraction not connected to background jobs.
- `MemoryProposalGuard` not connected to runtime Agent flow.
- `docs/skills` remains governance documentation, not a runtime policy engine.

## Current Proposal Baseline

Already available from Phase 6:

- Phase 6.2 `save_memory_preview` contract.
- `MemoryPreviewActionPayload`.
- `MemoryPreviewActionPayloadMapper`.
- `MemoryValidator`.
- Pending action preview-only confirm path.
- `save_memory_preview` confirm keeps `previewOnly=true`.
- `save_memory_preview` confirm keeps `wroteData=false`.
- `save_memory_preview` confirm does not create durable memory.
- Phase 6.4 `MemoryProposalGuard`.
- Phase 6.5 `MemoryExtractionService`.

Current proposal boundaries:

- `MemoryProposalGuard` is not connected to Agent runtime.
- `MemoryExtractionService` is not connected to runtime.
- Extraction does not automatically trigger.
- Extraction does not write memory store.
- Extraction does not write `life_events`.
- Durable memory write does not exist.
- No real Firestore memory repository exists.
- No production memory API endpoint exists.

## Proposal Runtime Integration Goals

Future proposal runtime integration should satisfy these goals:

- Extraction can only generate `save_memory_preview` proposals.
- Guard must run before a proposal enters pending action state.
- Low-confidence proposals must be blocked or marked `review_required`.
- Conflict proposals must be blocked or marked `review_required`.
- Sensitive proposals must be blocked or marked `review_required`.
- Every proposal must be user-visible before any confirmation.
- Confirm must not create durable memory until a separate Release Gate approves
  durable memory write.
- Every proposal must be auditable.
- Every proposal must be explainable.
- Every proposal flow must be reversible or discardable.
- No proposal path may bypass preview plus confirm.

Proposal runtime integration should not:

- Create active memory directly.
- Write `users/{userId}/memories`.
- Write `life_events`.
- Trigger read/write side effects from extraction alone.
- Change confirm semantics.
- Enable durable write flags.

## Candidate Trigger Sources

This section analyzes future trigger sources. It does not implement them.

### A. User Explicit Memory Intent

Examples:

- "帮我记一下"
- "以后记住"
- "这个偏好保存一下"
- "Remember that I prefer..."

Current status:

- Phase 6.2 can represent a `save_memory_preview` proposed action.
- Runtime Agent flow is not wired to invoke Memory extraction or guard.
- Confirm remains preview-only.

Future possible integration:

- Detect explicit memory intent through existing contract-driven intent flow.
- Build a candidate `MemoryPreviewActionPayload`.
- Validate the payload with `MemoryValidator`.
- Run `MemoryProposalGuard`.
- Create a user-visible `save_memory_preview` pending action only if allowed.
- Mark blocked or review-required proposals without durable writes.

Risks:

- Users may expect "remember" to persist immediately, while current and future
  safe behavior must remain preview plus confirm.
- Over-broad intent detection can produce noisy memory proposals.
- Sensitive user text can be accidentally proposed as memory.
- Runtime branches can grow if memory intent is handled with scattered if/else
  logic.

Priority recommendation:

- This is the safest first trigger candidate because the user explicitly asks for
  memory behavior.
- It still should not be implemented now.

### B. Timeline / `life_event` Extraction

Current status:

- Phase 6.5 can process local timeline-like inputs.
- Extraction is not connected to real `life_events`.
- Extraction does not write memory.
- Extraction does not run in background jobs.

Future possible integration:

- Select specific timeline or daily summary inputs.
- Run extraction only through a gated, audited flow.
- Emit `save_memory_preview` proposals.
- Require guard approval before pending action creation.
- Skip trivial, temporary, low-confidence, or sensitive content.

Why proposal-only:

- Raw timeline facts are not automatically durable memory.
- Summary/extraction can be wrong.
- User confirmation is required before long-term personalization.
- Direct active memory writes would bypass preview plus confirm.

Risks:

- One-off events can become false long-term memory.
- Emotional or transient statements can pollute durable memory.
- Daily summary errors can compound into proposals.
- High-volume extraction can produce proposal fatigue.

Priority recommendation:

- Useful later, but not first. It needs dashboard, audit, and rate-limit design
  before runtime integration.

### C. RAG / Chat Conversation Extraction

Current status:

- RAG and chat flows are active in the product.
- Memory extraction is not connected to RAG or chat conversations.
- Retrieval and proposal generation are not part of Planner prompt flow.

Future possible integration:

- Consider explicit user-selected conversation snippets.
- Allow proposal generation only when user asks to remember something or accepts
  a visible proposal.
- Keep document knowledge separate from personal memory.
- Store citation/source context in proposal metadata only after privacy review.

Risks:

- RAG document facts can be mistaken for user personal memory.
- Third-party or document content can pollute personal memory.
- Chat content may include sensitive material.
- Proposal generation from conversations can feel surprising if not visible.

Priority recommendation:

- Do not prioritize until explicit memory intent and user-visible memory
  management are designed.

### D. Daily Summary / Periodic Review

Current status:

- Phase 6.5 has local summary-like extraction.
- No daily summary runtime trigger is connected to memory extraction.
- No background scheduler exists for memory proposal generation.

Future possible integration:

- During a user-visible daily or weekly review, extract candidate memory
  proposals.
- Present proposals as a review queue.
- Apply guard decisions before display.
- Keep all outcomes preview-only.

Risks:

- Background generation can surprise users.
- Review queues can become noisy.
- Summary errors can create misleading proposals.
- Without forget/audit UI, users have poor control over memory lifecycle.

Priority recommendation:

- Defer until Memory Management / Forget / Audit design is available.

## Guard Placement Design

Future runtime proposal flow should use guard checks before pending action
creation.

Recommended logical order:

1. Identify candidate trigger.
2. Build candidate memory payload.
3. Validate payload with Phase 6.1 rules.
4. Run `MemoryProposalGuard`.
5. Decide one of:
   - `blocked`
   - `review_required`
   - `proposal_allowed`
   - `skipped`
6. Create pending `save_memory_preview` only when proposal is allowed.
7. Show proposal to the user.
8. Confirm remains preview-only until durable write Release Gate.

Guard requirements:

- Sensitive metadata must be blocked.
- Credential-like content must be blocked.
- Low confidence must be review-required or blocked.
- Conflicts must be review-required.
- Duplicate-like proposals must not auto-merge.
- Constraint proposals must remain stricter than ordinary preferences.

Guard output must be auditable but should not log full sensitive content.

## Pending Action and Confirm Semantics

Future pending action behavior:

- `actionType` remains `save_memory_preview`.
- `requiresConfirmation` remains `true`.
- The proposal is visible to the user.
- The payload has `previewOnly=true`.
- The payload is validated before pending creation.

Current confirm behavior must remain:

- `previewOnly=true`.
- `wroteData=false`.
- `createdResourceId=null`.
- No durable memory record.
- No `users/{userId}/memories` write.
- No repository call.
- No write flag enabled.

Durable memory write is not part of Phase 6.8. It requires a separate Release
Gate with explicit user approval, canary, rollback, smoke, audit, hard-delete,
and Firestore Rules validation.

## Proposal Audit and Explainability

Every future memory proposal should be explainable.

Minimum audit fields:

- Trigger source.
- Trigger type.
- Authenticated user id scope.
- Candidate memory type.
- Confidence.
- Importance.
- Guard decision.
- Guard reason.
- Source text reference or safe excerpt.
- Proposal creation time.
- Pending action id.
- Preview-only flag.

Logging boundaries:

- Do not log full sensitive content.
- Do not log tokens, credentials, or raw secrets.
- Redact source text when needed.
- Keep enough metadata to explain why a proposal was shown, blocked, or marked
  review-required.

## Rollback and Disable Strategy

Future proposal integration must be easy to disable.

Required design:

- Proposal generation disabled by default until approved.
- Single runtime gate for memory proposal generation.
- Per-trigger disable switches.
- Guard failure means no pending action.
- Extraction failure means no pending action.
- Validation failure means no pending action.
- Confirm flow remains independent from proposal generation.

Fallback behavior:

- If proposal runtime is disabled, current Agent behavior continues.
- If extraction fails, the Agent response should continue without memory
  proposal.
- If guard returns blocked, no durable side effect occurs.

## Test and Smoke Requirements

Before any future implementation can be considered complete:

- Unit tests for explicit memory intent proposal creation.
- Unit tests for extraction-to-proposal behavior.
- Unit tests proving guard runs before pending action creation.
- Unit tests for low-confidence / conflict / sensitive outcomes.
- Unit tests proving blocked proposals do not create pending actions.
- Unit tests proving proposals remain `save_memory_preview`.
- Unit tests proving confirm remains preview-only.
- Unit tests proving no `users/{userId}/memories` writes.
- Unit tests proving no `life_events` writes.
- Authenticated preview-only smoke.
- No-write smoke with proposal generation disabled.
- No-write smoke with proposal generation enabled in fake/local mode.
- Regression smoke for `smoke-agent-life-event-write`.
- Regression smoke for `smoke-rag-e2e`.

No test should require enabling durable memory write in Phase 6.8.

## Prohibitions

Do not do the following in Phase 6.8:

- Do not push.
- Do not deploy.
- Do not write business code.
- Do not modify `AgentRunner`.
- Do not modify `AgentIntentResolver`.
- Do not modify `AgentActionExecutor`.
- Do not modify `AgentContractValidator`.
- Do not modify `Program.cs`.
- Do not register `MemoryRepository`.
- Do not register `MemoryExtractionService`.
- Do not register `MemoryProposalGuard`.
- Do not connect real Firestore.
- Do not add a production API endpoint.
- Do not write `users/{userId}/memories`.
- Do not write `life_events`.
- Do not enable durable memory write.
- Do not modify Cloud Run environment variables.
- Do not modify Firestore Rules.
- Do not modify MCP.
- Do not enter Phase 6.8 implementation.
- Do not enter Phase 6.9.

## Recommended Future Implementation Shape

If the user later approves implementation, the lowest-risk sequence would be:

1. Add proposal runtime gate contracts.
2. Add trigger source enumeration.
3. Add guard decision plumbing before pending action creation.
4. Add explicit memory intent support only.
5. Prove no-write behavior.
6. Add extraction-to-preview only after proposal volume, audit, and user-visible
   review are designed.
7. Keep confirm preview-only until durable memory write Release Gate.

This is not approval to implement those steps now.

## Final Decision

Phase 6.8 currently allows Memory Proposal Runtime Integration Design only.

Current work must not connect proposal generation to runtime.

Current work must not connect extraction to runtime or background jobs.

Current work must not connect `MemoryProposalGuard` to Agent runtime.

Current work must not enable durable memory write.

Current work must not connect real Firestore.

Current work must not add production memory APIs.

Memory remains local-only / fake-only / preview-only.

Any move from this design into implementation or Phase 6.9 requires separate
user approval.

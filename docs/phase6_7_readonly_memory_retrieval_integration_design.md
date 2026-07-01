# Phase 6.7: Read-only Memory Retrieval Integration Design

Date: 2026-07-01

## Scope

Phase 6.7 designs future read-only Memory Retrieval integration. It does not
implement that integration.

This phase is design-only:

- No code changes.
- No runtime wiring.
- No durable write enablement.
- No real Firestore memory repository.
- No production API endpoint.
- No deployment.
- No Cloud Run environment changes.
- No Firestore Rules changes.
- No MCP changes.
- No `AgentRunner` / Planner connection.
- No prompt injection.

The goal is to define how Memory Retrieval can later enter Agent runtime in a
read-only, gated, reversible, observable, and user-safe way.

## Project Phase Sync

Current main phase: Phase 6 Memory Engine.

Current sub-stage:

- Phase 6.6 Runtime Wiring Design is complete and committed.

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

Current real state remains:

- Memory skeleton completed.
- Memory runtime not connected.
- Durable memory write disabled.
- Firestore memory repository not implemented / not registered.
- `AgentRunner` / Planner not connected to Memory Retrieval.
- Memory extraction not connected to background jobs.
- `docs/skills` remains governance documentation, not a runtime policy engine.

## Current Retrieval Baseline

Phase 6.3 already defines:

- `MemoryRetrievalRequest`.
- `MemoryRetrievalResult`.
- `IMemoryRetrievalService`.
- `InMemoryMemoryRetrievalService`.
- Local-only fake retrieval.
- Deterministic scoring / ranking.
- Unit tests for filtering, isolation, expiration, ordering, and limits.

Current retrieval boundaries:

- Retrieval service has no production DI registration.
- Retrieval service is not called by `AgentRunner`.
- Retrieval service is not connected to Planner.
- Retrieval service does not inject prompt context.
- Retrieval service does not call real Firestore.
- Retrieval service does not call a vector database.
- Retrieval service does not call an embedding model.
- Retrieval service does not mutate memory.
- Retrieval service does not create pending actions.
- Retrieval service does not trigger `save_memory_preview`.

## Read-only Integration Goals

Future read-only retrieval integration should satisfy these goals:

- Read only; never write memory.
- Never create pending actions.
- Never modify active memory.
- Never trigger extraction.
- Never trigger `save_memory_preview`.
- Never affect confirm flow.
- Never change durable write gates.
- Be explicitly enabled / disabled.
- Be observable through logs or structured trace fields.
- Be explainable through retrieval reasons and selected memory ids.
- Be auditable without exposing sensitive content in logs.
- Be reversible without deployment risk.
- Keep no-memory behavior equivalent to current Agent behavior.

Retrieval results must be treated as context, not instruction. Any future prompt
use must label memory as user-specific background and protect Planner behavior
from prompt-injection-like memory content.

## Candidate Runtime Placement

This section analyzes possible runtime placement. It does not approve or
implement integration.

### A. AgentRunner Pre-retrieval Context

Description:

- Retrieve memory before `AgentRunner` enters intent resolution / planning.
- Attach a memory context object to the Agent run.

Advantages:

- Centralized entry point.
- Easy to observe one retrieval decision per Agent run.
- Can provide consistent memory context to downstream planning.

Risks:

- It can pollute every intent, including intents that do not need memory.
- It can make `AgentRunner` grow again if retrieval filtering, prompt shaping,
  and fallback handling are all placed there.
- It can increase latency and token pressure for simple commands.
- It can accidentally make memory behavior global rather than intent-specific.

Does it pollute all intents:

- Yes, unless guarded by intent-aware retrieval eligibility and per-intent
  context budgets.

Does it risk expanding `AgentRunner`:

- Yes. This placement is only acceptable if `AgentRunner` delegates retrieval
  decisions to a small contract-driven collaborator.

Recommended now:

- Do not implement now.
- Keep as a candidate only after an explicit retrieval gate and truth map exist.

### B. After `AgentIntentResolver` / Before `BuildPlan`

Description:

- Resolve intent first.
- Decide whether read-only memory is eligible for that intent.
- Retrieve memory only for eligible intents before plan building.

Advantages:

- Intent-aware retrieval avoids running memory for every request.
- Better fit for token budgeting because the system knows the intended action
  family before retrieval.
- Keeps no-memory intents simpler.
- Can preserve Execution Contract Engine v1 if memory context is passed through
  explicit contract fields.

Risks:

- Memory-specific branches can leak into intent resolution.
- BuildPlan can become memory-aware in an ad hoc way.
- If intent classification is wrong, useful memory may be skipped or irrelevant
  memory may be included.

How to avoid breaking Execution Contract Engine v1:

- Keep memory eligibility as data, not branching logic.
- Add a typed retrieval decision object.
- Keep Planner input construction behind one contract boundary.
- Require retrieval-disabled fallback to produce current behavior.

Recommended now:

- Best design candidate for future read-only integration.
- Do not implement now.

### C. Planner Prompt Construction

Description:

- Retrieve or inject memory at the point where Planner prompt/context is built.

Advantages:

- Close to token-budget and context-shaping logic.
- Can keep retrieved memory out of earlier runtime stages.

Risks:

- Easy to hide retrieval behavior inside prompt construction.
- Harder to audit why memory was retrieved.
- Higher chance that memory content is treated as instruction.
- Can silently alter Planner behavior without user-visible boundaries.

Recommended now:

- Do not implement now.
- Future design should allow prompt construction to consume already-gated memory
  context, not perform retrieval itself.

### D. Tool-specific Retrieval

Description:

- Run retrieval only for selected tools or action families.

Advantages:

- Limits blast radius.
- Easy to start with low-risk read-only use cases.
- Avoids global Agent behavior changes.

Risks:

- Can scatter memory logic across tool implementations.
- Can create inconsistent behavior between tools.
- Can be hard to reason about if tool-specific retrieval is not represented in
  the runtime truth map.

Recommended now:

- Viable later as a constrained rollout technique.
- Do not implement now.

## Retrieval Gate Design

Future read-only retrieval must pass through an explicit gate before results can
reach planning.

The gate should decide:

- Whether retrieval is enabled.
- Whether the current intent is eligible.
- Which memory types are eligible.
- Which statuses are eligible.
- Whether temporary context has expired.
- Maximum result count.
- Maximum token budget.
- Redaction requirements.
- Whether sensitive types must be suppressed.
- Whether retrieval should be skipped for safety.

Gate output should include:

- `enabled`: true / false.
- `eligible`: true / false.
- `skipReason` when skipped.
- `allowedTypes`.
- `allowedStatuses`.
- `maxResults`.
- `maxContextTokens`.
- `redactionPolicy`.
- `auditMode`.

Gate output must not include secrets, raw tokens, or oversized memory payloads.

## Retrieval Context Shape

Future runtime should pass memory context as typed data before any prompt
rendering.

Candidate shape:

```text
MemoryRuntimeContext
- enabled
- eligible
- skippedReason
- results[]
- audit

MemoryRuntimeResult
- memoryId
- memoryType
- redactedContent
- score
- reason
- source
- updatedAt
```

Rules:

- Use redacted content for prompt candidates.
- Keep raw memory content out of logs.
- Keep retrieved memory ids available for audit.
- Preserve retrieval reasons for explainability.
- Never treat memory content as system instruction.

## Observability and Audit

Read-only retrieval should be observable without leaking sensitive user data.

Required telemetry design:

- Retrieval enabled / disabled.
- Retrieval eligible / skipped.
- Skip reason.
- Intent category.
- Result count.
- Memory types included.
- Token budget used.
- Latency.
- Redaction applied.
- No-write assertion.

Do not log:

- Full memory content.
- Secrets.
- Tokens.
- Credentials.
- Sensitive raw metadata.

Audit expectations:

- Each future Agent run with retrieval should be explainable by memory ids and
  retrieval reasons.
- Users should eventually have a way to inspect what memory influenced an Agent
  response before durable write is enabled broadly.

## Rollback and Disable Strategy

Read-only retrieval must be easy to disable.

Future implementation should support:

- Retrieval disabled by default until approved.
- A single gate to disable retrieval globally.
- Per-intent retrieval disable behavior.
- Retrieval failure fallback to no-memory Agent behavior.
- No dependency on retrieval for confirm flow.
- No durable side effects when retrieval is enabled.

Failure behavior:

- Retrieval timeout: skip memory and continue.
- Retrieval validation failure: skip memory and continue.
- Redaction failure: skip memory and continue.
- Gate denied: skip memory and continue.

The fallback path should be current Agent behavior.

## Test and Smoke Requirements

Before any future implementation can be considered complete, it must include:

- Unit tests for retrieval gate decisions.
- Unit tests for redaction and sensitive suppression.
- Unit tests for retrieval-disabled fallback.
- Unit tests proving retrieval does not create pending actions.
- Unit tests proving retrieval does not write memory.
- Unit tests proving retrieval does not trigger extraction.
- Authenticated preview-only smoke.
- No-write smoke with retrieval disabled.
- No-write smoke with retrieval enabled in fake/read-only mode.
- Regression smoke proving `smoke-agent-life-event-write` still passes.
- Regression smoke proving `smoke-rag-e2e` still passes.

Explicit no-write assertions:

- No `users/{userId}/memories` writes.
- No `life_events` writes.
- No `save_memory_preview` proposal generated by retrieval alone.
- No confirm flow change.
- No durable memory write flag enabled.

## Prohibitions

Do not do the following in Phase 6.7 design:

- Do not write code.
- Do not connect `InMemoryMemoryRetrievalService` to `AgentRunner`.
- Do not register `IMemoryRetrievalService` in production DI.
- Do not create `FirestoreMemoryRepository`.
- Do not write `users/{userId}/memories`.
- Do not add a production memory API endpoint.
- Do not inject memory into Planner prompts.
- Do not trigger extraction.
- Do not trigger `save_memory_preview`.
- Do not modify confirm flow.
- Do not change Cloud Run environment variables.
- Do not change Firestore Rules.
- Do not modify MCP.
- Do not deploy.
- Do not enter Phase 6.7 implementation.
- Do not enter Phase 6.8.

## Recommended Future Implementation Shape

If the user later approves implementation, the lowest-risk shape would be:

1. Add a retrieval gate contract.
2. Add a retrieval runtime context type.
3. Add retrieval-disabled no-op behavior.
4. Add fake/local retrieval simulation behind an explicit gate.
5. Add observability fields.
6. Prove no-write behavior.
7. Only then consider Planner context consumption.

This is not approval to implement those steps now.

## Final Decision

Phase 6.7 currently allows Read-only Memory Retrieval Integration Design only.

Current work must not connect retrieval to runtime.

Current work must not connect AgentRunner or Planner.

Current work must not inject memory into prompts.

Current work must not enable durable memory write.

Current work must not connect real Firestore.

Memory remains local-only / fake-only / preview-only.

Any move from this design into implementation requires separate user approval.

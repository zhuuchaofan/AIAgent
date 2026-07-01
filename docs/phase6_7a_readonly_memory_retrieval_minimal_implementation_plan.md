# Phase 6.7A: Read-only Memory Retrieval Minimal Implementation Plan

Date: 2026-07-01

## Scope

Phase 6.7A defines a minimal implementation plan for future read-only Memory
Retrieval runtime integration. It does not implement the plan.

This phase is planning-only:

- No code changes.
- No runtime wiring.
- No real Firestore connection.
- No deployment.
- No real write enablement.
- No durable memory write path.
- No production API endpoint.
- No `Program.cs` changes.
- No `AgentRunner` / Planner changes.
- No Memory service registration.

The output of this phase is a bounded execution plan that can be reviewed before
any implementation begins.

## Project Phase Sync

Current main phase: Phase 6 Memory Engine.

Current planning status:

- Phase 6.7 Read-only Memory Retrieval Integration Design is complete and
  committed.
- Phase 6.8 Memory Proposal Runtime Integration Design is complete and
  committed.
- The project is now preparing a plan for the first minimal implementation:
  Phase 6.7A Read-only Memory Retrieval Minimal Implementation.

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
- Phase 6.8 Memory Proposal Runtime Integration Design.

Current real state remains:

- Memory skeleton completed.
- Memory runtime not connected.
- Durable memory write disabled.
- Firestore memory repository not implemented / not registered.
- `AgentRunner` / Planner not connected to Memory Retrieval.
- Memory extraction not connected to background jobs.
- `MemoryProposalGuard` not connected to runtime Agent flow.
- `docs/skills` remains governance documentation, not a runtime policy engine.

## Implementation Objective

The future implementation objective is to build a minimal, reversible,
read-only Memory Retrieval runtime integration.

The first version may allow:

- Read-only retrieval.
- Explicit feature-flag control.
- Intent allowlist control.
- Maximum result count control.
- Safe context formatting boundaries.
- Fallback to current no-memory behavior.

The first version must not allow:

- Durable memory write.
- Pending action creation from retrieval.
- Extraction trigger.
- Automatic `save_memory_preview` creation.
- `users/{userId}/memories` write.
- `life_events` write.
- Real Firestore memory repository.
- Production memory API endpoint.

## Proposed Minimal Runtime Shape

The future implementation should add the smallest possible runtime seam for
read-only retrieval.

Candidate components:

- `IMemoryContextProvider` interface.
- No-op provider implementation.
- Fake/local provider implementation for gated test/runtime simulation.
- Feature flag reader.
- Intent allowlist decision.
- Maximum result count enforcement.
- Memory context formatter.
- Retrieval diagnostics / no-write trace fields.

Default behavior:

- Provider is disabled by default.
- Feature flags are off by default.
- Production remains no-memory by default.
- If provider is disabled, runtime behavior should match current Agent behavior.

### Should `AgentRunner` Change?

Potentially yes in a future implementation, but only minimally.

Expected boundary:

- `AgentRunner` may receive or request a typed `MemoryRuntimeContext`.
- It should not contain memory-specific branching logic.
- It should delegate feature flag, intent allowlist, retrieval, formatting, and
  fallback behavior to a small provider or context collaborator.

Risk:

- If retrieval logic is placed directly in `AgentRunner`, it can become a large
  memory-specific if/else router again.

### Should This Enter Through `ActionExecutor` / `ToolExecutor`?

Probably not for the first read-only integration.

Reasoning:

- Retrieval context is planning context, not an action execution side effect.
- `ActionExecutor` is better suited for proposed action construction and tool
  execution.
- Tool-specific retrieval can be considered later, but it risks scattering memory
  behavior across tools.

Boundary:

- Do not use executor layers to create pending actions from retrieval.
- Do not let retrieval trigger writes or proposals.

### Is a New `AgentExecutionContext` Needed?

Possibly.

Reasoning:

- A typed execution context can carry memory diagnostics without prompt injection.
- It can keep no-memory fallback explicit.
- It can separate runtime facts from prompt rendering.

Minimal shape:

- `MemoryEnabled`.
- `MemorySkippedReason`.
- `MemoryResultCount`.
- `MemoryContextPreview` or redacted formatted context.
- `Diagnostics`.

Risk:

- A broad execution context can become a dumping ground. Keep it narrowly scoped
  for read-only retrieval diagnostics.

### Avoiding AgentRunner if/else Growth

Required approach:

- Feature flag evaluation belongs outside branching-heavy runtime code.
- Intent allowlist belongs in a decision object.
- Provider returns a no-op context when disabled.
- Prompt formatting consumes a typed context only if later approved.
- Retrieval failure returns no-memory context instead of throwing through normal
  Agent flow.

## Feature Flags

Future implementation should define feature flags with default-off behavior.

Required flags:

- `ENABLE_MEMORY_RETRIEVAL`
- `ENABLE_MEMORY_CONTEXT_IN_AGENT`
- `MEMORY_RETRIEVAL_MAX_RESULTS`

Optional allowlist controls:

- User allowlist.
- Test user allowlist.
- Intent allowlist.

Rules:

- All flags default off.
- Production defaults to no-memory behavior.
- If `ENABLE_MEMORY_RETRIEVAL=false`, no retrieval occurs.
- If `ENABLE_MEMORY_CONTEXT_IN_AGENT=false`, retrieved memory cannot reach Agent
  context.
- If max results is unset or invalid, use a conservative default or zero.
- Flag-off behavior must exactly return to current no-memory runtime behavior.

## Files Expected To Change In Future Implementation

This section lists likely future files. This phase does not modify them.

### `LifeAgent.Api/Program.cs`

Why it may change:

- Future implementation may register a no-op or fake `IMemoryContextProvider`.
- Future implementation may bind feature flag settings.

Boundary:

- No real Firestore repository registration.
- No durable write services.
- Registration must default to no-op when flags are off.

Risk:

- Accidental production registration can make memory behavior active too early.

### `LifeAgent.Api/Services/Agent/AgentRunner.cs`

Why it may change:

- Future implementation may request a memory context before plan building.

Boundary:

- Minimal call only.
- No memory-specific branching.
- No pending action creation.
- No durable write behavior.

Risk:

- `AgentRunner` can regress into a large branch router.

### Agent execution contract / context files

Why they may change:

- Future implementation may need a typed `AgentExecutionContext` or diagnostics
  model to carry retrieval status.

Boundary:

- Context should hold diagnostics and redacted retrieval context only.
- No raw sensitive content in logs.
- No prompt injection without a separate approved step.

Risk:

- Over-broad context can blur runtime boundaries.

### New Memory Context Provider files

Why they may change:

- Add `IMemoryContextProvider`.
- Add no-op provider.
- Add fake/local provider.
- Add formatter / gate helper if needed.

Boundary:

- Read-only only.
- No repository writes.
- No pending actions.
- No extraction.

Risk:

- Provider can quietly become a write path unless interfaces forbid mutation.

### Agent response / diagnostics model

Why it may change:

- Future implementation may expose safe diagnostics for smoke or logs.

Boundary:

- No full memory content unless explicitly approved.
- Prefer counts, ids, reasons, and skip reasons.

Risk:

- Diagnostics can leak sensitive user memory.

### Tests

Why they may change:

- Add provider, flag, allowlist, fallback, and no-write tests.

Boundary:

- Tests must prove disabled behavior and no-write behavior.

Risk:

- Tests can pass local fake behavior while missing production flag defaults.

### Smoke scripts

Why they may change:

- Future no-write smoke may need to assert retrieval enabled/disabled behavior.

Boundary:

- No durable write smoke in Phase 6.7A.
- No real Firestore memory write.

Risk:

- Smoke can accidentally require production flags or mutate data.

### Docs

Why they may change:

- Update runtime truth map.
- Record smoke results.
- Record rollback procedures.

Boundary:

- Documentation must not imply durable write is enabled.

Risk:

- Docs can drift from actual runtime flag defaults.

## Tests Required Before Implementation Completion

Future implementation cannot be considered complete without:

- Unit tests for provider behavior.
- Feature flag off test.
- Intent allowlist test.
- Max result count test.
- Expired `temporary_context` exclusion.
- Archived memory exclusion.
- No pending action creation.
- No `wroteData=true`.
- No `users/{userId}/memories` write.
- RAG regression.
- `life_event` preview-only regression.
- `save_memory_preview` regression.
- Authenticated smoke.
- Retrieval timeout fallback test.
- Malformed retrieval result fallback test.
- Diagnostics redaction test.

## No-write Smoke Design

Future smoke should prove read-only behavior.

Required scenarios:

- Retrieval disabled fallback:
  - Agent behavior remains current no-memory behavior.
  - No pending action is created by retrieval.
  - No memory write occurs.

- Retrieval enabled fake/local mode:
  - Retrieval can return context.
  - `wroteData=false`.
  - No pending action is created by retrieval.
  - No `users/{userId}/memories` write.
  - No `life_events` write.
  - RAG still works.
  - `life_event` preview-only still works.
  - `save_memory_preview` still confirms preview-only.

- Production safety:
  - Cloud Run flags for real write remain disabled.
  - Durable memory write flags are absent or false.
  - Mock auth remains disabled.
  - No Firestore Rules change is required for the smoke.

Expected smoke outputs:

- Retrieval enabled / disabled state.
- Result count or skipped reason.
- `wroteData=false`.
- No created memory resource id.
- No pending action created by retrieval alone.

## Rollback Plan

Future implementation must support these rollback paths:

- Flag-off rollback:
  - Set `ENABLE_MEMORY_RETRIEVAL=false`.
  - Set `ENABLE_MEMORY_CONTEXT_IN_AGENT=false`.
  - Runtime returns to no-memory behavior.

- Provider disabled fallback:
  - No-op provider returns an empty memory context.
  - Agent flow continues.

- Timeout fallback:
  - Retrieval timeout returns no-memory context.
  - Agent flow continues.

- Malformed retrieval result fallback:
  - Invalid result is discarded.
  - Diagnostics record skip reason.
  - Agent flow continues.

- Deployment rollback:
  - Revert to previous Cloud Run revision only after explicit deployment
    approval in a future task.

- Smoke failure handling:
  - Stop.
  - Do not broaden rollout.
  - Do not enable durable write.
  - Record failure and keep retrieval disabled.

## Implementation Stop Conditions

Future implementation must stop immediately if it requires:

- Real Firestore memory repository.
- Durable memory write.
- New production memory API.
- Firestore Rules modification.
- Cloud Run write flags.
- Runtime extraction wiring.
- Automatic prompt injection of sensitive memory.
- Changing confirm semantics.
- Writing `users/{userId}/memories`.
- Writing `life_events`.

Future implementation must also stop if:

- No-write smoke fails.
- RAG regression fails.
- `life_event` regression fails.
- `save_memory_preview` regression fails.
- Feature flag off behavior does not match current behavior.

## Anti-Patterns

Forbidden for the first implementation:

- Directly connecting real Firestore.
- Retrieval automatically triggering `save_memory_preview`.
- Retrieval automatically triggering extraction.
- Retrieval automatically triggering durable write.
- Enabling retrieval by default without flags.
- Mixing memory context with RAG citations.
- Making `AgentRunner` a large if/else memory router again.
- Treating implementation as complete without no-write smoke.
- Logging full memory content in diagnostics.
- Creating production memory API endpoints.
- Adding write flags as part of read-only retrieval.

## Final Decision

Phase 6.7A only completes the minimal implementation plan.

Current work does not allow implementation.

Future Phase 6.7A implementation may begin only after separate user
confirmation.

The first implementation may only be read-only.

Durable memory write remains reserved for a separate Release Gate.

Memory remains local-only / fake-only / preview-only until implementation is
explicitly approved and completed under no-write constraints.

# Phase 6.2 to 6.5 Execution Plan

This document defines the staged execution route after Phase 6.1 Memory Taxonomy & Schema.
It is a planning document only. It must not be treated as approval to implement Phase 6.2,
6.3, 6.4, or 6.5.

## Global Gates

- Phase 6.2 through Phase 6.5 must not be merged into one large implementation.
- Each phase must stop after completion and wait for explicit user confirmation before the next phase starts.
- No phase may automatically enter the following phase.
- No real Firestore memory repository is allowed.
- No production API endpoint is allowed.
- No Cloud Run deployment is allowed.
- No real memory write flag may be enabled.
- No durable memory write is allowed unless a later phase is explicitly approved for it.
- No flow may bypass preview plus confirm semantics.
- Memory-specific branches must not be pushed back into `AgentRunner`.
- Firestore Rules, Cloud Run env, MCP servers, and production write paths are out of scope.

## Phase 6.2: Memory Proposal & Confirm Contract

### 1. Goal

Define the `save_memory_preview` `proposedAction` contract for memory proposals and connect it to
pending action preview semantics without creating any durable memory write path.

### 2. Allowed File Scope

- Agent contract constants and DTO/model files that describe preview-only proposed actions.
- Pending action contract tests.
- Documentation under `docs/`.
- Unit tests that verify contract shape and preview-only behavior.

### 3. Prohibitions

- Do not create a real Firestore memory repository.
- Do not write to `users/{userId}/memories`.
- Do not add production API endpoints.
- Do not register memory services in `Program.cs` for production runtime use.
- Do not modify Cloud Run env or Firestore Rules.
- Do not deploy.
- Do not enable real writes.
- Do not add memory-specific branching inside `AgentRunner`.

### 4. Input

- User text that implies a memory candidate.
- Existing pending action preview contract.
- Phase 6.1 memory taxonomy and validator definitions.

### 5. Output

- A stable preview-only `save_memory_preview` proposed action schema.
- A pending action payload that can be reviewed by tests as contract data.
- No durable memory record.

### 6. Test Requirements

- `save_memory_preview` proposed action contains memory candidate fields aligned with Phase 6.1.
- Proposed action requires confirmation.
- Preview output does not write memory data.
- Invalid proposed action shape fails contract tests.
- Existing Agent preview contracts remain stable.
- Tests must not touch real Firestore.

### 7. Completion Standard

- Contract shape is documented and covered by unit tests.
- Preview-only behavior is proven by tests.
- No production runtime write path exists.

### 8. Commit Permission

Commit is allowed after review and tests pass.

### 9. Next Phase Gate

Must stop after Phase 6.2 and wait for explicit user confirmation before Phase 6.3 starts.

## Phase 6.3: Memory Retrieval Skeleton

### 1. Goal

Define a memory retrieval interface and a local retrieval skeleton using the fake repository only.
This phase establishes retrieval contracts without connecting a real vector store or changing real Planner behavior.

### 2. Allowed File Scope

- Memory retrieval interface and local fake implementation files.
- Tests for retrieval filtering, ranking placeholders, and user isolation.
- Documentation under `docs/`.
- Preview-only contract tests if needed to protect existing behavior.

### 3. Prohibitions

- Do not connect a real vector database or embedding-backed memory retrieval.
- Do not use real Firestore.
- Do not modify real Planner behavior unless protected behind preview-only contract tests.
- Do not add production API endpoints.
- Do not deploy.
- Do not enable real writes.
- Do not add memory-specific branches back into `AgentRunner`.

### 4. Input

- User id.
- Query text or local retrieval request object.
- Fake in-memory memory records created inside tests.

### 5. Output

- Local retrieval result objects from fake data only.
- No production Planner mutation.
- No durable memory read or write.

### 6. Test Requirements

- Retrieval is scoped by `userId`.
- Retrieval can filter by type and status where applicable.
- Retrieval returns stable local result objects.
- Cross-user retrieval is rejected or returns no data.
- Tests must not touch real Firestore, vector stores, API endpoints, or Cloud Run.

### 7. Completion Standard

- Interface and fake skeleton compile.
- Tests prove local-only retrieval behavior and user isolation.
- No production retrieval path is connected.

### 8. Commit Permission

Commit is allowed after review and tests pass.

### 9. Next Phase Gate

Must stop after Phase 6.3 and wait for explicit user confirmation before Phase 6.4 starts.

## Phase 6.4: Merge / Conflict / Pollution Guard

### 1. Goal

Define local-only merge candidate, conflict result, and pollution guard rules for memory proposals.
This phase should reduce duplicate or unsafe proposals without automatically modifying active memory.

### 2. Allowed File Scope

- Local memory merge/conflict/pollution guard models.
- Rule-based local evaluator services.
- Unit tests for duplicate, conflict, pollution, and sensitive metadata cases.
- Documentation under `docs/`.

### 3. Prohibitions

- Do not automatically modify active memory.
- Do not write to Firestore.
- Do not connect production write paths.
- Do not add production API endpoints.
- Do not deploy.
- Do not enable real writes.
- Do not bypass preview plus confirm.
- Do not put memory-specific branching into `AgentRunner`.

### 4. Input

- Existing memory proposal object.
- Local fake memory candidates.
- Phase 6.1 validator result.

### 5. Output

- Merge candidate result.
- Conflict result.
- Pollution guard decision.
- No changed active memory record.

### 6. Test Requirements

- Duplicate-like proposals produce merge candidates.
- Contradictory proposals produce conflict results.
- Sensitive or raw-payload proposals are blocked by pollution guard.
- Constraint memories keep stricter handling.
- Active memory is not modified by evaluator tests.
- Tests must remain local-only and fake-only.

### 7. Completion Standard

- Local rules are deterministic and covered by unit tests.
- No durable mutation occurs.
- Preview/confirm boundary remains intact.

### 8. Commit Permission

Commit is allowed after review and tests pass.

### 9. Next Phase Gate

Must stop after Phase 6.4 and wait for explicit user confirmation before Phase 6.5 starts.

## Phase 6.5: Timeline / Summary Extraction Skeleton

### 1. Goal

Define a skeleton for producing memory proposals from Timeline and daily summary inputs.
The output must be a `save_memory_preview` proposed action or a local proposal object only.

### 2. Allowed File Scope

- Local extraction request/response models.
- Skeleton extraction service that returns proposal objects only.
- Unit tests for Timeline and daily summary extraction shapes.
- Documentation under `docs/`.

### 3. Prohibitions

- Do not write to the memory store.
- Do not write to `life_events`.
- Do not create production extraction endpoints.
- Do not connect real Firestore writes.
- Do not deploy.
- Do not enable real writes.
- Do not bypass preview plus confirm.
- Do not add memory-specific branches back into `AgentRunner`.

### 4. Input

- Timeline event DTOs or local test fixtures.
- Daily summary DTOs or local test fixtures.
- Phase 6.1 taxonomy and validation rules.

### 5. Output

- `save_memory_preview` proposed action, or
- Local memory proposal object.
- No memory store mutation.
- No Timeline or daily summary mutation.

### 6. Test Requirements

- Timeline-like inputs can produce local proposal objects.
- Daily-summary-like inputs can produce local proposal objects.
- Invalid or sensitive proposal content is rejected by validator or guard rules.
- Output remains preview-only.
- Tests must not touch real Firestore, production API endpoints, Cloud Run, or MCP.

### 7. Completion Standard

- Extraction skeleton produces typed proposal output only.
- Tests prove no memory or life event writes occur.
- Documentation states that production extraction remains unapproved.

### 8. Commit Permission

Commit is allowed after review and tests pass.

### 9. Next Phase Gate

Must stop after Phase 6.5 and wait for explicit user confirmation before any later phase starts.

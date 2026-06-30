# Phase 6.4: Memory Merge / Conflict / Pollution Guard

Phase 6.4 defines a local-only guard for memory proposals. It evaluates whether a
proposal looks duplicate-like, contradictory, polluted, hostile, or too risky to pass
without review. It returns decisions only.

## Scope

- Define `MemoryMergeCandidate`.
- Define `MemoryConflictResult`.
- Define `MemoryPollutionDecision`.
- Define `IMemoryProposalGuard`.
- Implement `MemoryProposalGuard` as a local rule evaluator.
- Cover duplicate, conflict, pollution, constraint, and no-mutation behavior with unit tests.

## Inputs

- `MemoryPreviewActionPayload` from the Phase 6.2 preview contract.
- Existing fake/local `Memory` objects from Phase 6.1 model data.
- Phase 6.1 `MemoryValidator` rules.

## Outputs

- `MemoryMergeCandidate`
- `MemoryConflictResult`
- `MemoryPollutionDecision`

The guard only returns decisions. It does not modify active memory, create durable
memory, or write `users/{userId}/memories`.

## Local Rules

Duplicate-like proposal:

- same `memoryType`;
- normalized content token overlap above a local threshold;
- returns `Action = "merge_candidate"`;
- never automatically merges.

Contradictory proposal:

- same or related memory type;
- positive and negative preference markers over overlapping content;
- returns `Action = "review_required"` with a `MemoryConflictResult`;
- never overwrites existing memory.

Pollution guard:

- reuses Phase 6.1 `MemoryValidator` for metadata and content safety;
- blocks sensitive metadata such as `password`, `token`, `apiKey`, `secret`, `authorization`, or `credential`;
- blocks raw or payload-shaped metadata;
- blocks obvious credential content through validator rules;
- marks low-confidence proposals as `review_required`;
- marks user-hostile inferred facts as `review_required`.

Constraint stricter rules:

- `constraint` must use `importance = 5`;
- low-confidence constraints require review;
- constraint conflicts require review;
- no constraint proposal is allowed to bypass preview plus confirm.

## Non-Goals

- No active memory mutation.
- No Firestore writes.
- No real Firestore memory repository.
- No production API endpoint.
- No AgentRunner connection.
- No Planner connection.
- No prompt changes.
- No Cloud Run env changes.
- No Firestore Rules changes.
- No deployment.
- No real write enablement.
- No preview plus confirm bypass.
- No Phase 6.5 Timeline or Summary extraction.
- No Memory Dashboard.
- No MCP.

## Test Coverage

Unit tests cover:

- duplicate-like proposals produce merge candidates;
- duplicate-like evaluation does not mutate existing memory;
- contradictory proposals produce conflict results;
- sensitive metadata is blocked;
- raw payload metadata is blocked;
- low-confidence proposals require review;
- hostile inferred facts require review;
- constraint importance below 5 is blocked;
- low-confidence constraints require review;
- constraint conflicts require review;
- clean proposals are allowed without mutating existing memory.

## Completion Gate

Phase 6.4 is complete only after:

- local-only evaluator compiles;
- unit tests cover core rules;
- no durable mutation is introduced;
- preview/confirm boundary remains intact;
- `dotnet test` passes;
- `git diff --check` passes;
- review confirms no Firestore, API, AgentRunner, Planner, deployment, or write path was introduced;
- the user explicitly approves commit.

After Phase 6.4 completes, work must stop. Phase 6.5 requires separate user confirmation.

# Phase 6.5: Timeline / Summary Extraction Skeleton

Phase 6.5 defines a local-only extraction skeleton that converts local Timeline-like
and daily-summary-like inputs into preview memory proposals. It does not connect to
production runtime behavior.

## Scope

- Define Timeline extraction input.
- Define Summary extraction input.
- Define `MemoryExtractionRequest`.
- Define `MemoryExtractionResult`.
- Define `IMemoryExtractionService`.
- Implement `MemoryExtractionService` as a local rule-based skeleton.
- Validate proposals with Phase 6.1 Memory rules.
- Guard proposals with Phase 6.4 pollution rules.
- Cover proposed, skipped, rejected, and review-required outcomes with tests.

## Inputs

- Local `TimelineMemoryExtractionInput` fixtures.
- Local `SummaryMemoryExtractionInput` fixtures.
- Phase 6.1 Memory taxonomy and validator.
- Phase 6.4 Memory proposal guard.

## Outputs

- `MemoryPreviewActionPayload` proposal objects.
- `MemoryExtractionResult` objects with status:
  - `proposed`
  - `skipped`
  - `rejected`
  - `review_required`

Outputs are local only. They are not written to any durable store.

## Extraction Rules

Timeline-like input:

- only clear, stable, long-term-value statements can produce proposals;
- trivial events such as one-off meals or coffee purchases are skipped;
- raw life event facts are not treated as durable memory by default;
- output remains preview-only.

Summary-like input:

- statements such as preferences, goals, habits, projects, constraints, relationships,
  and temporary contexts can produce proposals;
- temporary emotional complaints are skipped;
- sensitive or low-confidence content returns `rejected` or `review_required`.

Current skeleton proposal coverage:

- `preference`
- `goal`
- `habit`
- `relationship`
- `project`
- `constraint`
- `temporary_context`

This is not full 12-type extraction coverage.

## Safety

- metadata sensitive keys are rejected;
- raw payload metadata is rejected;
- credential-like content is rejected by Phase 6.1 validator rules;
- low-confidence proposals require review;
- `constraint` proposals use `importance=5`;
- `temporary_context` proposals require `expiresAt`.

## Non-Goals

- No memory store writes.
- No `users/{userId}/memories` writes.
- No `life_events` writes.
- No real Firestore.
- No real Firestore repository.
- No production API endpoint.
- No AgentRunner connection.
- No Planner connection.
- No prompt changes.
- No Cloud Run env changes.
- No Firestore Rules changes.
- No MCP changes.
- No deployment.
- No real write enablement.
- No preview plus confirm bypass.
- No Memory Dashboard.
- No real Daily Summary job.
- No background scheduler.

## Test Coverage

Unit tests cover:

- Timeline-like input generates preview-only proposal;
- daily-summary-like input generates preview-only proposal;
- trivial Timeline event is skipped;
- temporary emotional complaint is skipped;
- sensitive metadata is rejected;
- raw payload metadata is rejected;
- low-confidence content returns `review_required`;
- constraint proposal uses `importance=5`;
- temporary context without `expiresAt` is rejected;
- output does not write memory store or `life_events`;
- no Firestore, API endpoint, AgentRunner, or Planner connection.

## Completion Gate

Phase 6.5 is complete only after:

- local-only extraction skeleton compiles;
- unit tests cover the expected local outcomes;
- no durable mutation is introduced;
- preview/confirm boundary remains intact;
- `dotnet test` passes;
- `git diff --check` passes;
- review confirms no Firestore, API, AgentRunner, Planner, deployment, scheduler, or write path was introduced;
- the user explicitly approves commit.

Any later phase requires separate user confirmation.

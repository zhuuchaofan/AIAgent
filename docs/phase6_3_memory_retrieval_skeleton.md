# Phase 6.3: Memory Retrieval Skeleton

Phase 6.3 defines a local-only Memory Retrieval skeleton. It uses Phase 6.1
`InMemoryMemoryRepository` data for deterministic unit tests and does not connect
production runtime behavior.

## Scope

- Define `MemoryRetrievalRequest`.
- Define `MemoryRetrievalResult`.
- Define `IMemoryRetrievalService`.
- Implement `InMemoryMemoryRetrievalService` as a fake/local retrieval service.
- Cover user isolation, filters, expiration, scoring, ordering, and limits with unit tests.

## Request Schema

`MemoryRetrievalRequest` fields:

- `userId`: required authenticated user id.
- `query`: optional text query. Empty query is allowed and returns a stable filtered list.
- `types`: optional MemoryType snake_case filters.
- `statuses`: optional MemoryStatus snake_case filters.
- `limit`: maximum result count. Defaults to `5` when non-positive and is capped.
- `includeArchived`: defaults to `false`.

Default behavior:

- only `active` memories are searched when `statuses` is empty;
- archived memories are excluded unless `includeArchived=true`;
- expired `temporary_context` memories are excluded.

## Result Schema

`MemoryRetrievalResult` fields:

- `memoryId`
- `memoryType`
- `content`
- `confidence`
- `importance`
- `score`
- `source`
- `updatedAt`
- `reason`

## Interface

```csharp
Task<IReadOnlyList<MemoryRetrievalResult>> RetrieveAsync(
    MemoryRetrievalRequest request,
    CancellationToken cancellationToken = default);
```

## Local Fake Retrieval Rules

The fake service:

- requires `userId`;
- reads only through `IMemoryRepository.ListByUserAsync`;
- never queries Firestore directly;
- never calls an embedding model;
- never calls a vector database;
- never writes data;
- filters by `types` when provided;
- filters by `statuses`, defaulting to `active`;
- excludes archived memory unless `includeArchived=true`;
- excludes expired `temporary_context`;
- matches query tokens with deterministic content `contains` checks;
- allows empty query for stable filtered list retrieval.

## Placeholder Scoring

The score is deterministic and local-only:

- each query token content match adds score;
- higher `importance` adds score;
- higher `confidence` adds score;
- recently updated memories receive a small recency score;
- ties are resolved by importance, updated time, and `memoryId`.

This scoring is not semantic retrieval and is not a vector-ranking replacement.

## Non-Goals

- No real Firestore memory repository.
- No Firestore `users/{userId}/memories` reads or writes.
- No vector database.
- No embedding model call.
- No production API endpoint.
- No `Program.cs` registration.
- No AgentRunner or Planner integration.
- No Planner prompt injection.
- No Cloud Run env changes.
- No Firestore Rules changes.
- No deployment.
- No real write enablement.
- No Phase 6.4 merge/conflict/pollution guard.
- No Timeline or daily summary extraction.
- No Memory Dashboard.
- No MCP.

## Test Coverage

Unit tests cover:

- `userId` is required;
- user isolation;
- default active-only retrieval;
- archived exclusion by default;
- archived inclusion with `includeArchived=true`;
- expired `temporary_context` exclusion;
- MemoryType filter;
- MemoryStatus filter;
- content query matching;
- empty-query stable listing;
- importance, confidence, and updated-time ordering;
- limit enforcement.

## Completion Gate

Phase 6.3 is complete only after:

- unit tests pass locally;
- `git diff --check` passes;
- review confirms no Firestore, vector, API, AgentRunner, Planner, deployment, or write path was introduced;
- the user explicitly approves commit.

After Phase 6.3 completes, work must stop. Phase 6.4 requires separate user confirmation.

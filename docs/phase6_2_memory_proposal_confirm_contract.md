# Phase 6.2: Memory Proposal & Confirm Contract

Phase 6.2 implements the preview-only contract for `save_memory_preview`.
It does not approve durable memory writes and does not start Phase 6.3.

## Scope

- Define a stable `save_memory_preview` proposedAction payload.
- Validate the payload against Phase 6.1 Memory taxonomy and validator rules.
- Keep pending action confirmation preview-only.
- Prove the contract with unit tests.

## Payload Schema

`save_memory_preview` payload:

```json
{
  "memoryType": "preference",
  "content": "I prefer writing code in the morning.",
  "confidence": 0.8,
  "importance": 3,
  "source": "agent_preview",
  "previewOnly": true,
  "originalMessage": "User's original message",
  "sourceText": "Optional source text",
  "metadata": {
    "proposalStage": "phase6_2_preview_contract"
  },
  "expiresAt": null
}
```

Field requirements:

- `memoryType` must be one of the Phase 6.1 `MemoryType` snake_case values.
- `content` is required and cannot be blank.
- `confidence` must be between `0.0` and `1.0`.
- `importance` must be between `1` and `5`.
- `source` identifies preview origin and does not mean durable persistence.
- `previewOnly` must be `true`.
- `originalMessage` records the user text that produced the proposal.
- `sourceText` is optional.
- `metadata` is optional and must pass Phase 6.1 metadata safety rules.
- `expiresAt` is optional except for `temporary_context`, where it is required and must be in the future.

## Contract Validation

The contract validator enforces:

- `actionType == "save_memory_preview"`.
- `requiresConfirmation == true`.
- payload `previewOnly == true`.
- payload maps to a Phase 6.1 `Memory` with status `pending_confirm` for validation.
- `temporary_context` requires a future `expiresAt`.
- `constraint` requires `importance == 5`.
- metadata keys must not contain `password`, `token`, `apiKey`, `secret`, `authorization`, or `credential`,
  including bypass variants such as `api_key` or `api-key`.
- raw or oversized metadata payloads are rejected by the Phase 6.1 validator.
- content credential guard remains conservative and preview-only.

## Confirm Behavior

Confirming `save_memory_preview`:

- transitions the pending action lifecycle to `confirmed`;
- returns `previewOnly=true`;
- returns `wroteData=false`;
- returns `createdResourceId=null`;
- does not create a memory record;
- does not write `users/{userId}/memories`;
- does not call a memory repository;
- does not enable any write flag.

`ConfirmWriteCompletedAsync` explicitly rejects `save_memory_preview` with `preview_only`.

## Non-Goals

- No real Firestore memory repository.
- No production API endpoint.
- No `Program.cs` or DI memory service registration.
- No Firestore Rules changes.
- No Cloud Run env changes.
- No deployment.
- No real write enablement.
- No memory retrieval.
- No merge/conflict/pollution guard.
- No Timeline or daily summary extraction.
- No Memory Dashboard.
- No memory-specific branches pushed into `AgentRunner`.

## Test Coverage

Phase 6.2 tests cover:

- memory intent returns `save_memory_preview`;
- proposed action requires confirmation;
- proposed action payload has `previewOnly=true`;
- payload aligns with Phase 6.1 Memory schema;
- invalid memory preview payload is rejected by contract validation;
- confirm keeps `wroteData=false`;
- confirm keeps `createdResourceId=null`;
- `ConfirmWriteCompletedAsync` rejects `save_memory_preview`;
- existing life_event, reminder, and RAG contracts continue to pass.

## Completion Gate

Phase 6.2 is complete only after:

- tests pass locally;
- `git diff --check` passes;
- review confirms no durable memory write path was introduced;
- the user explicitly approves commit.

After Phase 6.2 completes, work must stop. Phase 6.3 requires separate user confirmation.

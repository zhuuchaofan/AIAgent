# Phase 6.8A Dedicated Memory Proposal Smoke Script Plan

Date: 2026-07-02

## Scope

This document plans a future dedicated smoke script for Phase 6.8A guarded
Memory preview proposal runtime.

This task does not:

- Add a smoke script.
- Modify code.
- Deploy.
- Enable durable Memory write.
- Connect a real Firestore Memory runtime.
- Modify Cloud Run env.
- Modify Firestore Rules.
- Modify MCP.

## Current State

- Phase 6.8A guarded Memory preview proposal runtime is implemented.
- Phase 6.8A authenticated smoke has passed.
- Existing `smoke-agent-life-event-write`: PASS.
- Existing `smoke-rag-e2e`: PASS.
- There is currently no dedicated Phase 6.8A Memory proposal smoke script.
- Current Memory proposal guarded behavior is mainly covered by `dotnet` tests.
- Durable Memory write is not enabled.
- Real Firestore Memory runtime is not connected.
- `users/{userId}/memories` is not written.
- `life_events` is not written by Memory proposal runtime.
- Cloud Run env, Firestore Rules, and MCP were not modified.

## Why Dedicated Smoke Is Needed

Existing smoke coverage verifies:

- life_event preview-only flow.
- Authenticated RAG / Agent Preview flow.

Existing smoke does not directly verify:

- `save_memory_preview` guarded proposal behavior.
- `allowed`, `review_required`, and `blocked` proposal outcomes.
- Flag-off serialized payload contract.
- Blocked proposal not creating a pending action.
- `review_required` proposal remaining user-visible.

A dedicated smoke script is needed before calling Phase 6.8A production-smoke
coverage complete.

## Proposed Script Name

Future script name:

```text
scripts/smoke-memory-proposal-preview.mjs
```

This phase only plans the script. It does not create the file.

## Required Inputs

Future script inputs:

- `API_BASE_URL`
- `FIREBASE_ID_TOKEN`
- Optional flags expectation.
- No fake token.
- No mock auth.
- No mock LLM.

The script must not require durable write flags.

## Test Scenarios

### A. Default-off baseline

Expected behavior:

- Explicit memory intent returns `save_memory_preview`.
- Payload does not contain `guardDecision`.
- Payload does not contain `blocked`.
- Payload does not contain `reviewRequired`.
- Payload does not contain `guardReason`.
- Payload does not contain `conflictResult`.
- Payload does not contain `mergeCandidate`.
- Confirm returns `previewOnly=true`.
- Confirm returns `wroteData=false`.

### B. Guard flags off online behavior

Expected behavior:

- Online response contract is unchanged.
- No `users/{userId}/memories` write.
- No `life_events` write.
- No durable Memory write.

### C. Guarded allowed proposal

If a dedicated preview-only / canary / local test environment enables guard
flags, expected behavior:

- Allowed proposal creates a pending action.
- Payload may contain safe guard diagnostics.
- Confirm remains `previewOnly=true`.
- Confirm remains `wroteData=false`.
- No durable Memory write.

### D. Review-required proposal

Expected behavior:

- Pending action is created.
- `ReviewRequired=true` is user-visible.
- Conflict / merge candidate information is explainable.
- Proposal is not auto-merged.
- Existing memory is not auto-overwritten.
- No durable Memory write.

### E. Blocked proposal

Expected behavior:

- Pending action is not created.
- Response remains `previewOnly=true`.
- Response remains `wroteData=false`.
- Blocked reason is explainable.
- No durable Memory write.

## No-write Assertions

Future script must assert:

- No `wroteData=true`.
- No `users/{userId}/memories` write.
- No `life_events` write from proposal runtime.
- No durable Memory write.
- No extraction trigger.
- No background proposal.
- No RAG/chat automatic proposal.

## Environment Rules

Future script rules:

- Do not enable durable write in ordinary production smoke.
- Do not enable `ENABLE_AGENT_WRITE_TOOLS`.
- Do not enable `ENABLE_CREATE_LIFE_EVENT_TOOL`.
- If guard flags must be tested on, use a separate preview-only / canary / local
  test environment.
- Do not modify Firestore Rules for smoke.
- Do not use mock auth instead of a Firebase token.
- Do not use mock LLM.

## Result Document Future Path

If the script is implemented and executed later, write results to:

```text
docs/phase6_8a_dedicated_memory_proposal_smoke_result.md
```

Do not create that result document until the smoke script exists and is
executed.

## Stop Conditions

Stop future dedicated smoke immediately if any of these occur:

- `wroteData=true`.
- `createdResourceId` points to memory.
- `users/{userId}/memories` is written.
- `life_events` is written by proposal runtime.
- Blocked proposal creates a pending action.
- `review_required` is not user-visible.
- Flag-off response outputs guard fields.
- Mock auth is required.
- Firestore Rules modification is required.
- Cloud Run write flags are required.

## Final Conclusion

A dedicated Memory proposal smoke is needed before calling Phase 6.8A
production-smoke coverage complete.

Current authenticated smoke passed using existing scripts. Durable Memory write
remains disabled. Real Firestore Memory runtime remains disconnected.

Script implementation requires separate user approval.

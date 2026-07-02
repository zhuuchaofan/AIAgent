# Phase 6.8A Post-implementation Local Verification Plan

Date: 2026-07-02

## Scope

This plan defines pre-deployment local verification for Phase 6.8A guarded
Memory preview proposal runtime code.

This plan does not:

- Deploy any service.
- Modify Cloud Run environment variables.
- Modify Firestore Rules.
- Connect a real Firestore Memory repository.
- Enable durable Memory write.
- Enter the durable Memory write Release Gate.
- Push commits.

## Current State

Phase 6.8A implementation is complete and committed.

Implementation commit:

- `77910ea479d0db36bbfcff60bca3f3add319e04e`

Phase 6.8A result docs are complete and committed.

Current implementation state:

- Guarded Memory preview proposal runtime is implemented.
- `ENABLE_MEMORY_PROPOSAL_RUNTIME` defaults to `false`.
- `ENABLE_MEMORY_PROPOSAL_GUARD` defaults to `false`.
- Flag off keeps `save_memory_preview` behavior unchanged.
- Flag off serialized payload does not output guard fields.
- Flag on only enables guarded preview proposal behavior.
- `allowed` proposals create `save_memory_preview` pending actions.
- `review_required` proposals create pending actions and remain user-visible.
- `blocked` proposals do not create pending actions.
- Confirming `save_memory_preview` remains `previewOnly=true`.
- Confirming `save_memory_preview` remains `wroteData=false`.
- Real Firestore Memory runtime is not connected.
- No production API endpoint was added.
- `users/{userId}/memories` is not written.
- `life_events` is not written.
- Extraction is not triggered.
- Durable Memory write is not enabled.
- No deployment was performed.
- No push was performed.

## Verification Goals

Local verification must prove:

- Default-off behavior remains unchanged.
- No-env / flags-off runtime does not output guard fields.
- Serialized `save_memory_preview` payload does not contain
  `guardDecision`, `blocked`, `reviewRequired`, `guardReason`,
  `conflictResult`, or `mergeCandidate`.
- Flags-on `allowed` proposal creates a pending action.
- Flags-on `review_required` proposal creates a pending action and remains
  user-visible.
- Flags-on `blocked` proposal does not create a pending action.
- Confirming `save_memory_preview` remains `previewOnly=true`.
- Confirming `save_memory_preview` remains `wroteData=false`.
- No `users/{userId}/memories` write occurs.
- No `life_events` write occurs.
- No extraction trigger occurs.
- RAG behavior has no regression.
- life_event preview behavior has no regression.
- read-only Memory retrieval has no regression.

## Local Test Matrix

### A. No env / default-off

Expected result:

- Proposal runtime is disabled.
- Guard is disabled.
- `save_memory_preview` payload contains no guard fields.
- Response contract is unchanged.
- Confirm remains `previewOnly=true` and `wroteData=false`.
- No durable Memory write occurs.

### B. `ENABLE_MEMORY_PROPOSAL_RUNTIME=false`

Expected result:

- Same as default-off behavior.
- Guard does not run.
- Guard fields do not appear in serialized payload.
- Existing preview proposal contract remains unchanged.

### C. `ENABLE_MEMORY_PROPOSAL_RUNTIME=true` and `ENABLE_MEMORY_PROPOSAL_GUARD=false`

Expected result:

- Guard does not run.
- Guard fields do not appear in serialized payload.
- Runtime still creates only preview proposals.
- Durable Memory write remains disabled.

### D. Both proposal flags true + allowed proposal

Expected result:

- Guard runs.
- `allowed` proposal creates a `save_memory_preview` pending action.
- Payload may include safe guard diagnostics.
- No memory data is written.
- Confirm remains preview-only.

### E. Both proposal flags true + review_required proposal

Expected result:

- Pending action is created.
- Payload contains `ReviewRequired=true`.
- Conflict or merge information is user-visible.
- Proposal is not auto-merged.
- Existing memory is not auto-overwritten.
- No memory data is written.

### F. Both proposal flags true + blocked proposal

Expected result:

- Pending action is not created.
- Response remains `previewOnly=true`.
- Response remains `wroteData=false`.
- Blocked reason is explainable.
- No memory data is written.

## Required Commands

Required local verification commands:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
git diff --check
git status --short
```

Optional focused test areas:

- `AgentRunner_MemoryIntent_ReturnsPhase62PreviewPayloadSchema`
- `AgentRunner_MemoryProposalGuardAllowed_CreatesPendingAction`
- `AgentRunner_MemoryProposalGuardBlocked_DoesNotCreatePendingAction`
- `AgentRunner_MemoryProposalGuardReviewRequired_RecordsConflictWithoutWriting`
- `AgentRunner_MemoryProposalGuardMergeCandidate_RecordsCandidateWithoutAutoMerge`
- `AgentConfirmEndpoint_SaveMemoryPreviewConfirmsWithoutDurableMemoryWrite`
- Read-only Memory retrieval regression tests.
- RAG regression tests.
- life_event preview regression tests.

## Smoke Readiness Criteria

Preview-only API deployment smoke may be considered only after:

- `dotnet test` passes.
- `git status` is clean.
- Default-off serialized contract is verified.
- Guarded proposal behavior is verified.
- No-write behavior is verified.
- RAG regression passes.
- life_event regression passes.
- read-only Memory retrieval regression passes.
- Cloud Run write flags remain unchanged.
- Firestore Rules remain unchanged.
- MCP remains unchanged.
- The user explicitly approves deployment.

## No-write Checklist

Before any deployment smoke:

- `users/{userId}/memories` write: must remain false.
- `life_events` write from proposal runtime: must remain false.
- Extraction trigger: must remain false.
- Background proposal: must remain false.
- RAG/chat auto proposal: must remain false.
- Durable Memory write: must remain false.
- Real Firestore Memory repository: must remain absent.
- Confirm `save_memory_preview` `wroteData`: must remain false.

## Stop Conditions

Stop verification and do not proceed if any of these occur:

- Default-off serialized payload contains guard fields.
- Blocked proposal creates a pending action.
- `review_required` proposal is not user-visible.
- Confirming `save_memory_preview` returns `wroteData=true`.
- Proposal runtime writes `users/{userId}/memories`.
- Proposal runtime writes `life_events`.
- Extraction is triggered.
- RAG regression appears.
- life_event regression appears.
- read-only retrieval regression appears.
- Any Cloud Run, Firestore Rules, or MCP change is required.

## Final Conclusion

Phase 6.8A local verification plan is ready.

This plan does not deploy. It does not enable durable Memory write. It does not
connect a real Firestore Memory runtime. Preview-only deployment smoke requires
separate user approval.

# Phase 6.8A Memory Proposal Runtime Implementation Result

Date: 2026-07-02

## Scope

This document records the Phase 6.8A Memory Proposal Runtime Minimal
Implementation result and no-write verification.

This result does not mean:

- Durable Memory write is enabled.
- A real Firestore Memory repository is connected.
- Background extraction is enabled.
- The durable Memory write Release Gate has passed.
- Memory Dashboard / Forget / Audit is implemented.

## Implementation Summary

Phase 6.8A implemented a minimal guarded preview proposal runtime for explicit
`save_memory_preview` memory intents.

Implemented behavior:

- Added `MemoryProposalRuntimeOptions`.
- Runs `MemoryProposalGuard` before `save_memory_preview` pending action
  creation when proposal runtime flags are enabled.
- `allowed` proposals create a `save_memory_preview` pending action.
- `review_required` proposals create a pending action and carry user-visible
  guard information.
- `blocked` proposals do not create a pending action.
- `MemoryPreviewActionPayload` now supports guard metadata.
- Guard fields are nullable and use `JsonIgnore(WhenWritingNull)`.
- Flag-off / no-env serialized payload does not contain guard fields.
- Confirming `save_memory_preview` remains preview-only.
- Confirming `save_memory_preview` still returns `wroteData=false`.

## Changed Files

Implementation commit files:

- `LifeAgent.Api/Models/Agent/MemoryPreviewActionPayload.cs`
- `LifeAgent.Api/Program.cs`
- `LifeAgent.Api/Services/Agent/AgentActionExecutor.cs`
- `LifeAgent.Api/Services/Agent/AgentRunner.cs`
- `LifeAgent.Api/Services/Memories/MemoryProposalRuntimeOptions.cs`
- `LifeAgent.Tests/AgentSkeletonTest.cs`

## Feature Flag Result

Runtime flags:

- `ENABLE_MEMORY_PROPOSAL_RUNTIME` defaults to `false`.
- `ENABLE_MEMORY_PROPOSAL_GUARD` defaults to `false`.

Flag behavior:

- Flag off keeps current `save_memory_preview` behavior unchanged.
- Flag off serialized payload does not output guard fields.
- Flag on only enables guarded preview proposal behavior.
- Flag on does not enable durable write.
- Flag on does not trigger automatic extraction.
- Flag on does not connect a real Firestore Memory repository.

## Guard Behavior Result

Guard decision behavior:

- `allowed`: creates a `save_memory_preview` pending action.
- `review_required`: creates a pending action and exposes
  `ReviewRequired=true` plus guard information.
- `blocked`: does not create a pending action.
- Blocked response remains `previewOnly=true` and `wroteData=false`.
- Merge candidate is recorded but not auto-merged.
- Conflict requires review and does not auto-overwrite existing memory.

## No-write Verification

| Check | Result |
| --- | --- |
| Real Firestore Memory repository connected | no |
| Writes `users/{userId}/memories` | no |
| Writes `life_events` | no |
| Adds production API endpoint | no |
| Triggers extraction | no |
| Automatically creates proposals from RAG/chat/background | no |
| Enables durable Memory write | no |
| Modifies Cloud Run env | no |
| Modifies Firestore Rules | no |
| Modifies MCP | no |
| Deploys or pushes | no |

## Contract Verification

Verified contract behavior:

- Default-off serialized payload does not contain guard fields.
- Tests use `JsonSerializer` plus `JsonDocument` to verify JSON shape.
- Confirming `save_memory_preview` remains `previewOnly=true`.
- Confirming `save_memory_preview` remains `wroteData=false`.
- `createdResourceId` remains null / empty for preview-only memory confirm.
- `AgentRunner` did not become a memory-specific if/else router.
- Execution Contract Engine v1 remains intact.

## Test Result

Command:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

Result:

- Passed: 323
- Failed: 0
- Skipped: 0

Diff check:

- `git diff --check`: passed

Known warnings:

- Existing nullable warnings remain in `LifeAgent.Api/Services/RagChatService.cs`.

## Known Notes / Non-blocking Risks

- If proposal runtime continues to expand, extract a dedicated
  `MemoryProposalService` so `AgentActionExecutor` does not keep growing.
- Blocked response currently has `ProposedAction=null` while the contract action
  type remains `save_memory_preview`; a future pass can introduce a clearer
  blocked proposal response model.
- This is still not durable Memory write.
- This still does not include Memory Dashboard / Forget / Audit.
- This still does not include a real Firestore Memory repository.

## Final Conclusion

Phase 6.8A implementation is complete.

The guarded Memory preview proposal runtime is implemented for explicit
`save_memory_preview` proposal flow. Default-off contract behavior remains
unchanged. No durable Memory write is enabled. No real Firestore Memory runtime
is connected.

Any next step requires separate user approval.

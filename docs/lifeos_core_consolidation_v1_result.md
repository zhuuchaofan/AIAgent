# LifeOS Core Consolidation v1 Result

Date: 2026-07-11

## Executive Summary

LifeOS Core Consolidation v1 keeps the current Personal Home pending action
path as the only active mainline, downgrades older Agent Preview confirmation
objects to legacy support, and leaves the codebase ready to start Memory Engine
v1 without adding another pending action model.

Current conclusion: **ready for preview deployment after standard validation**.

## Current Mainline

The current LifeOS Personal Home pending action mainline is:

```text
LifeOS Personal Home
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> FirestorePendingActionStore
  -> users/{userId}/pendingActions/{pendingActionId}
```

This is the only path that should be used for user-visible pending action state,
confirmation, cancellation, and refresh persistence.

## Consolidation Changes

- Kept the Personal Home pending action card as the primary UI surface.
- Moved the old Agent / RAG preview input below the pending action history and
  labelled it as technical diagnostics.
- Clarified in the UI that the diagnostics area is not the Personal Home
  pending action mainline.
- Added backend comments marking the legacy `/api/agent/run`,
  `/api/agent/confirm`, `PendingAgentAction`, and `IPendingAgentActionStore`
  surface as legacy Agent Preview support.
- Marked `Phase80PendingActionRuntime` as a compatibility name that now owns the
  LifeOS Personal Home pending action runtime mainline.

## Legacy Kept

| Legacy area | Files | Why kept | Current status |
| --- | --- | --- | --- |
| Old Agent Preview run | `LifeAgent.Api/Endpoints/AgentEndpoints.cs`, `life-agent-web/src/app/actions/knowledge.ts` | Still useful for technical RAG/tool-call diagnostics and existing tests. | Legacy diagnostics only; not the Personal Home pending action mainline. |
| Old confirm endpoint | `/api/agent/confirm` in `AgentEndpoints.cs` | Covered by existing Agent / life event confirmation tests and still guards the old write flow. | Legacy; Personal Home must use `/api/agent/pending-actions/{actionId}/confirm`. |
| `PendingAgentAction` model | `LifeAgent.Api/Models/Agent/PendingAgentAction.cs` | Required by old Agent Preview and confirmation tests. | Legacy support model. |
| `IPendingAgentActionStore` | `LifeAgent.Api/Services/Agent/IPendingAgentActionStore.cs` | Required by old Agent Preview runner, confirmation coordinator, and tests. | Legacy store contract. |
| `FirestorePendingAgentActionStore` | `LifeAgent.Api/Services/Agent/FirestorePendingAgentActionStore.cs` | Supports the old confirm path and should not be removed without a dedicated migration. | Legacy; not used by Personal Home mainline. |
| `Phase80PendingActionRuntime` name | `LifeAgent.Api/Services/Agent/Phase8/Phase80PendingActionRuntime.cs` | Renaming would churn tests, docs, and DI without changing behavior. | Retained compatibility name; current mainline runtime. |

## Safety Boundary

Personal Home pending actions:

- do not call `/api/agent/confirm`
- do not use `IPendingAgentActionStore`
- do not use `FirestorePendingAgentActionStore`
- do not enter `AgentLifeEventConfirmationWriteCoordinator`
- do not write `memories`
- do not write `life_events`
- do not execute real tool actions

The legacy `/api/agent/confirm` route still exists, but its life event write
branch remains behind the existing feature gate:

```text
ENABLE_AGENT_WRITE_TOOLS=true
ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

Those flags must stay disabled unless a separate release gate explicitly
approves real life event writes.

## UI Naming

The visible product surface remains:

- `LifeOS Personal Home`
- `个人助手`
- `LifeOS 个人助手`

`Agent Preview` now appears only as a technical diagnostics concept for the old
RAG/tool-call preview area.

## Documentation Positioning

Historical Phase 7, Phase 8, and Phase 9 documents remain useful as rollout
evidence, but new development should treat the following documents as the
current orientation source:

- `docs/personal_agent_v2_closeout.md`
- `docs/lifeos_personal_home_v1_result.md`
- `docs/lifeos_personal_home_v1_deployment_result.md`
- `docs/lifeos_core_consolidation_v1_result.md`

Older phase-specific Firestore Rules, emulator, and fake-first preview docs are
not deleted because they preserve decision history. They should be read as
historical context, not as current implementation instructions, when they
conflict with the mainline above.

## Current Non-goals

This consolidation did not:

- implement Memory Engine v1
- write `users/{userId}/memories`
- write `life_events`
- execute real tools
- modify Cloud Run env
- modify `firestore.rules`
- modify `firebase.json`
- modify package files or lockfiles
- deploy

## Memory Engine v1 Entry Point

Memory Engine v1 should start from the existing pending action mainline, not
from legacy Agent Preview confirmation:

```text
memory proposal
  -> /api/agent/pending-actions
  -> user confirm
  -> future users/{userId}/memories write
```

Recommended next step:

1. Define the memory proposal model and validation rules.
2. Represent proposed memory writes as pending actions.
3. Reuse `IPendingActionStore` and `FirestorePendingActionStore` for approval
   state.
4. Keep real memory writes disabled until a dedicated release gate approves:
   - Firestore schema
   - write rules / server-only write policy
   - Cloud Run env
   - authenticated smoke
   - rollback plan

## Validation

Required validation for this stage:

- `git status --short`
- `git diff --stat`
- `git diff --check`
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
- `npm --prefix life-agent-web run lint`

# LifeOS Project Consolidation Map

Date: 2026-07-12

## Purpose

This document maps the project phases, current code ownership, active docs, and
remaining goals into one current-state index. It is intended to prevent future
work from confusing historical preview-only phases with the current deployed
Unified Inbox mainline.

Authoritative companion docs:

- `docs/skills/lifeos-phase-assessment.md`
- `docs/skills/_shared/phase-map.md`
- `docs/lifeos_unified_inbox_current_design.md`
- `docs/skills/cloud-run-deploy.md`

## Current One-line State

LifeOS has completed the foundation, life data layer, RAG, Agent Preview,
pending-action persistence, and the first minimal real write:

```text
Unified Inbox life_record Confirm -> life_events
```

It is still not a fully autonomous personal Agent. Memory durable writes,
Reminder writes, Tool Execution, external side effects, and MCP remain closed.

## Phase / Code / Docs Map

| Area | Status | Main code | Active docs | Notes |
|---|---|---|---|---|
| Phase 1 Foundation | Complete | `Program.cs`, middleware, auth provider, Cloud Run config | `docs/phase1/*`, `docs/skills/cloud-run-deploy.md` | Web/API/Auth/Firestore/Cloud Run base. |
| Phase 2 Life Data | Complete | `LifeEndpoints.cs`, `LifeEventService.cs`, `ReminderService.cs`, `DailySummaryService.cs`, `Timeline.tsx`, `ReminderWidget.tsx` | `docs/phase2/*` | Manual life records, reminders, daily summary. |
| Phase 3 RAG | Complete | `DocumentEndpoints.cs`, `RagChatEndpoints.cs`, `RagSearchService.cs`, `RagChat.tsx`, `KnowledgeBase.tsx` | `docs/phase3/*` | Document upload, processing, vector search, citations, chat history. |
| Phase 3.5 Stabilization | Absorbed | rate limiting, validators, deployment docs, tests | `docs/phase3_5/*`, `docs/skills/development/*` | Non-feature hardening. |
| Phase 4 Agent MVP | Complete / legacy-compatible | `AgentRunner.cs`, `ToolExecutor.cs`, `/api/agent/run`, `/api/agent/confirm` | `docs/phase4/*` | Legacy Agent Preview path still exists, but is not the home mainline. |
| Phase 5 Agent Write MVP | Live for LifeEvent only | `Phase80PendingActionRuntime.cs`, `IUnifiedInboxIntentClassifier`, `Phase80LifeEventConfirmWriteExecutor`, `IPendingActionStore` | `docs/lifeos_unified_inbox_current_design.md` | Current home mainline. Confirmed life records write `life_events`. |
| Release Gate | Partially passed | Cloud Run revisions, Firestore writer, production smoke | `docs/phase5/*`, `docs/phase9_personal_agent_v2_release_gate.md` | LifeEvent minimal write approved/deployed. Other write targets remain No-Go. |
| Phase 6 Memory Engine | Skeleton / next | `Services/Memories/*`, `MemoryPreviewActionPayload`, memory guard/retrieval skeletons | `docs/phase6_*` | Memory proposals/retrieval skeletons exist. Durable Memory write is not enabled. |
| Phase 7+ Tool Runtime | Architecture / skeletons | `Services/Agent/GuardedExecution/*`, `ToolRegistry.cs`, pending action interfaces | `docs/phase7_*` | Useful contracts and tests, not a license to execute external tools. |
| Phase 8/9 Pending Action | Historical foundation now absorbed | `IPendingActionStore`, `FirestorePendingActionStore`, `Phase80PendingActionRuntime` | `docs/phase8_*`, `docs/phase9_*` | Docs are historical unless explicitly updated. Current truth is Unified Inbox doc. |

## Current Production Mainline

```text
life-agent-web/src/components/AgentPreview.tsx
  -> life-agent-web/src/app/actions/knowledge.ts
  -> POST /api/agent/pending-actions
  -> AgentEndpoints.CreatePhase80PendingActionAsync
  -> Phase80PendingActionRuntime
  -> IUnifiedInboxIntentClassifier
  -> IPendingActionStore
  -> Confirm
  -> Phase80LifeEventConfirmWriteExecutor only for life_record_preview
```

## Current Write Matrix

| Candidate | Classifier may create | Confirm creates pending status | Confirm writes durable data | Current durable target |
|---|---:|---:|---:|---|
| Life record | yes | yes | yes | `users/{userId}/life_events` |
| Reminder | yes | yes | no | none |
| Memory | candidate-only | yes when preview path produces it | no | none |
| Plan | yes | yes | no | none |
| Tool / external action | possible as high-risk candidate | no execution | no | none |

## Docs Policy Going Forward

1. New agents should read `docs/lifeos_project_consolidation_map.md` before
   touching Agent, Memory, or Unified Inbox code.
2. Historical phase docs should not be edited for every current behavior change.
   Instead, update:
   - `docs/skills/lifeos-phase-assessment.md`
   - `docs/skills/_shared/phase-map.md`
   - `docs/lifeos_unified_inbox_current_design.md`
   - this consolidation map.
3. If an old doc says preview-only but current code says LifeEvent write is
   live, current-state docs win.
4. New write targets require their own Release Gate section before production
   deployment.

## Code Cleanup Backlog

These are cleanup tasks, not prerequisites for current production operation:

1. Wrap or rename `Phase80PendingActionRuntime` to `UnifiedInboxRuntime`.
2. Move classifier contracts out of the Phase8 file into
   `Services/Agent/UnifiedInbox/`.
3. Split `Phase80LifeEventConfirmWriteExecutor` into its own file.
4. Hide or remove `/api/agent/pending-actions/demo` compatibility aliases after
   tests and docs stop using them.
5. Build a dedicated lightweight intent-classification prompt instead of
   reusing `ILlmService.ParseAsync`.
6. Add authenticated production smoke for:
   - journal text with future time mention -> life record
   - explicit reminder command -> reminder preview
   - life record Confirm -> appears in recent life records
7. Decide the next approved Release Gate:
   - reminder write
   - durable memory write
   - tool execution

## Final Goal Path

The final target remains a personal life Agent:

1. Unified Inbox stable for record/reminder/memory/tool candidates.
2. LifeEvent write stable and observable.
3. Reminder write Release Gate.
4. Memory Engine durable write Release Gate.
5. Agent context uses LifeEvents, Reminders, RAG, and Memory safely.
6. Planning and recommendation workflows produce pending actions.
7. External integrations are added one by one behind explicit gates.
8. No autonomous side effect happens without confirm, audit, and rollback story.

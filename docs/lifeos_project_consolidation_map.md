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
- `docs/memory_durable_write_release_gate_readiness.md`
- `docs/memory_review_inbox_state_release_gate.md`
- `docs/skills/cloud-run-deploy.md`

## Current One-line State

LifeOS has completed the foundation, life data layer, RAG, Agent Preview,
pending-action persistence, and two minimal user-confirmed real writes:

```text
Unified Inbox life_record Confirm -> life_events
Memory Review kept candidate Remember -> memories
```

It is still not a fully autonomous personal Agent. Automatic Memory writes,
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
| Release Gate | Partially passed | Cloud Run revisions, Firestore writer, production smoke | `docs/phase5/*`, `docs/phase9_personal_agent_v2_release_gate.md` | LifeEvent minimal write approved/deployed. Memory Review minimal write approved locally. Other write targets remain No-Go. |
| Phase 6 Memory Engine | Minimal write gate | `Services/Memories/*`, `MemoryPreviewActionPayload`, memory guard/retrieval skeletons, `/api/memory/*` | `docs/phase6_*`, `docs/memory_review_inbox_state_release_gate.md`, `docs/memory_durable_write_release_gate_readiness.md` | Home AI insights and Memory Review Inbox can surface memory signals. Kept review candidates can be explicitly remembered; automatic Memory write remains closed. |
| Phase 7+ Tool Runtime | Architecture / skeletons | `Services/Agent/GuardedExecution/*`, `ToolRegistry.cs`, pending action interfaces | `docs/phase7_*` | Useful contracts and tests, not a license to execute external tools. |
| Phase 8/9 Pending Action | Historical foundation now absorbed | `IPendingActionStore`, `FirestorePendingActionStore`, `Phase80PendingActionRuntime` | `docs/phase8_*`, `docs/phase9_*` | Docs are historical unless explicitly updated. Current truth is Unified Inbox doc. |

## Current Production Mainline

```text
life-agent-web/src/components/AgentPreview.tsx
  -> life-agent-web/src/app/actions/knowledge.ts Unified Inbox pending-action actions
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

## Current Memory Preview Surface

Phase 6 currently exposes product-facing preview surfaces only:

```text
GET /api/memory/insights/preview
  -> read recent life_events
  -> return up to 3 user-facing AI insights
  -> no Memory write

GET /api/memory/review-inbox/preview
  -> read recent life_events
  -> return candidate memory signals with source summaries and review status
  -> no durable Memory write

POST /api/memory/review-inbox/{candidateId}/keep|dismiss
  -> persist review UI state to users/{userId}/memory_review_items
  -> no durable Memory write

POST /api/memory/review-inbox/{candidateId}/remember
  -> require kept candidate
  -> validate and guard edited memory content
  -> write users/{userId}/memories/{memoryId}

GET /api/memory/context/preview
  -> read recent life_events
  -> return read-only context items for product and RAG validation
  -> no Memory write

GET /api/memory/items
  -> list current user's durable memories
  -> default active only

POST /api/memory/items/{memoryId}/archive
  -> archive current user's memory
  -> archived memories are excluded from ordinary context

POST /api/life/chat
  -> read recent life_events and active memories
  -> answer user's life questions in read-only mode
  -> no life_events write, no Memory write, no Reminder write, no Tool execution

POST /api/life/review
  -> read recent life_events and active memories
  -> return structured recent-life review cards with source event ids
  -> supports recent / today / week review windows
  -> no life_events write, no Memory write, no Reminder write, no Tool execution

POST /api/life/review/cards/keep
  -> convert one review card into a kept Memory Review candidate
  -> write only users/{userId}/memory_review_items review state
  -> no durable Memory write
```

The web product surfaces are:

- Home `AI 发现`: a lightweight preview of repeated themes.
- `/memory/review`: a candidate inbox where the user can inspect, keep, or hide signals.
- `/memory`: the user's confirmed durable memories, with archive/forget.
- `/life/chat`: read-only life Q&A based on recent life records and active
  memories, with product feedback when remembered content contributed to the
  answer context.
- `/life/review`: read-only recent-life review generated by
  `POST /api/life/review`, with recent / today / week windows, optional
  evidence expansion from source life records, and a "worth remembering"
  bridge into Memory Review Inbox.
- `/chat`: knowledge-base answers may receive active durable memories as auxiliary personal background, and the UI links to `/memory` when such background exists. Citations still come only from retrieved document Chunks.

Only `remember` creates durable Memory records. Keep/dismiss only persist Review
Inbox state so the user's decision survives refresh.
Knowledge-base Q&A may use active durable memories as background, but Memory
never becomes a document citation source.
Life Q&A may use recent `life_events` and active Memory as private context, but
it does not persist chat history or create new data. Archived or expired Memory
is excluded from ordinary Life Q&A context.
Life Review may use recent `life_events` and active Memory as private context,
but it returns only read-only review cards and evidence references. A review
card may be kept as a Memory Review candidate, which writes review state only;
durable Memory still requires the explicit Memory Review remember flow.
Durable Memory write preparation is tracked in
`docs/memory_durable_write_release_gate_readiness.md`.

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
2. Hide or remove `/api/agent/pending-actions/demo` compatibility aliases after
   tests and docs stop using them.
3. Add authenticated production smoke for:
   - journal text with future time mention -> life record
   - explicit reminder command -> reminder preview
   - life record Confirm -> appears in recent life records
4. Decide the next approved Release Gate:
   - reminder write
   - durable memory write
   - tool execution

Completed cleanup:

- Moved Unified Inbox classifier contracts to `Services/Agent/UnifiedInbox/`.
- Replaced the reused event parser with a dedicated JSON-only Unified Inbox
  intent classifier prompt.
- Split `Phase80LifeEventConfirmWriteExecutor` into its own LifeEvents file.
- Removed the old web manual ingest component, `/debug` pending action
  diagnostics page, and unused frontend wrappers for legacy agent preview
  endpoints. Backend compatibility routes and safety audit fields remain.
- Split the knowledge-base Q&A UI into smaller `rag-chat` components while
  keeping `/chat` scoped to document Q&A plus non-cited Memory background.

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

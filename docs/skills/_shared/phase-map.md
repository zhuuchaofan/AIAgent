# LifeOS Phase Map

## When to use

Use this whenever judging current project state, classifying a request, planning a feature, reviewing docs, or deciding whether work belongs to a Release Gate.

## Goal

Keep AI agents aligned with LifeOS phase-based development and prevent conflating RAG, Agent, Memory, and production enablement work.

## Inputs

- `docs/skills/lifeos-phase-assessment.md`
- `docs/lifeos_project_roadmap.md`
- Relevant Phase docs under `docs/phase*/`
- Current user request.

## Process

1. Read the authoritative Phase assessment skill when Phase status matters.
2. Classify the request into the lowest applicable Phase or Release Gate.
3. Check whether the requested work is design, implementation, stabilization, deployment, or production enablement.
4. State the Phase boundary before implementation when scope is ambiguous.

## Current Phase map

| Area | Status | Meaning |
|---|---|---|
| Phase 3 RAG | Complete | Knowledge access layer: document upload, chunking, embeddings, vector retrieval, RAG answer, citations, history. |
| Phase 4 Agent MVP | Complete | Agent Preview, read tools, controlled Agent runner, pending action confirmation lifecycle. |
| Phase 5 Agent Write MVP | LifeEvent and gated Reminder minimal writes live | Unified Inbox can create a server-side pending action and Confirm `life_record_preview` into `life_events`; confirmed reminders can write `reminders` only when the reminder write gate is enabled and a due time exists. |
| Unified Inbox | Current product mainline | Home input -> intent classifier -> pending action -> confirm gate -> allowlisted executor. See `docs/lifeos_unified_inbox_current_design.md`. |
| Release Gate | LifeEvent and Reminder minimal gates passed; Memory Review minimal write approved locally | Reminder delivery/scheduling, automatic Memory writes, external tools, MCP, and Cloud Run env changes remain separately approved gates. |
| Phase 6 Memory Engine | Current development context | Long-term Memory taxonomy, preview proposals, Review Inbox state persistence, explicit remember action, memory management, Memory Value Loop, retrieval skeletons, merge/conflict/pollution guard, and candidate hygiene that separates stable, observing, and likely one-off signals. Automatic Memory write and tool-style runtime integration require separate approval. |

## Release Gate boundary

Release Gate work is not a normal development Phase. The minimal `life_events` write gate for Unified Inbox has been approved and deployed. The minimal reminder write gate may write `reminders` only after explicit user confirmation and concrete due-time parsing. The minimal Memory Review `remember` gate may write durable Memory only after explicit user confirmation. New side-effect targets still require a gate: reminder delivery/scheduling, automatic Memory writes, external tools, MCP, Cloud Run env changes, production Firestore rules changes, and new integrations.

Development can prepare code, docs, tests, and preview-only validation. It cannot silently cross into real production writes.

## Red lines

- Do not rename or redefine Phases ad hoc.
- Do not call Phase 3 RAG "Agent化".
- Do not treat LifeEvent or Reminder minimal write enablement as permission to open reminder delivery, automatic Memory, Tool, MCP, or external writes.
- Do not place real-write canary inside Phase 6 implementation.
- Do not mark design-only Memory work as implemented durable Memory.

## Done criteria

- The request is mapped to a Phase or Release Gate.
- Phase scope, non-goals, and safety boundary are clear.
- If implementation proceeds, it matches the current Phase boundary.

## Checklist

- [ ] Phase 3 is RAG / knowledge access only.
- [ ] Phase 4/5 Agent capabilities are separated from each production write target.
- [ ] Phase 6 Memory work distinguishes design, preview, and durable write.
- [ ] Release Gate actions require explicit approval.
- [ ] Final wording does not overstate current state.

## Related skills

- `core/phase-management.md`
- `_shared/project-glossary.md`
- `_shared/safety-red-lines.md`
- `core/preview-confirm-write.md`

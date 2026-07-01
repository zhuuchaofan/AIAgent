# LifeOS Project Skills

This directory is the project-level operating guide for AI coding agents working on LifeOS / LifeAgent.

The goal is to let a new agent understand the project workflow without reading historical chats, keep work aligned with the current Phase, and preserve the safety boundary between design, preview-only behavior, and real production writes.

## Directory structure

```text
docs/skills/
  _shared/       Shared vocabulary, phase map, red lines, and checklists.
  core/          Always-on project collaboration rules.
  development/  Testing and commit readiness rules.
  playbooks/    Ordered skill combinations for common workflows.
```

Existing source-of-truth documents remain valid:

- `docs/skills/lifeos-phase-assessment.md` is the authoritative Phase assessment skill.
- `docs/skills/cloud-run-deploy.md` is the authoritative Cloud Run deployment skill.

## New agent first-read order

New agents should read the shared and core baseline rules first, then route to development or playbook Skills based on the task.

1. `_shared/phase-map.md`
2. `_shared/safety-red-lines.md`
3. `core/project-overview.md`
4. `core/phase-management.md`
5. `core/safety-boundaries.md`
6. `core/architecture-first.md`
7. `core/docs-sync.md`
8. `core/preview-confirm-write.md`
9. `development/testing.md`
10. `development/commit-readiness.md`

## Common task routing

| Task | Skills to use |
|---|---|
| Determine current project stage | `core/phase-management.md`, `_shared/phase-map.md` |
| Start a new feature | `playbooks/new-feature-flow.md` |
| Close a Phase or subphase | `playbooks/phase-closeout-flow.md` |
| Design cross-module behavior | `core/architecture-first.md`, `core/docs-sync.md` |
| Add or change write behavior | `core/preview-confirm-write.md`, `core/safety-boundaries.md` |
| Validate a change | `development/testing.md` |
| Prepare a local commit | `development/commit-readiness.md` |
| Deploy to Cloud Run | `docs/skills/cloud-run-deploy.md`, `core/safety-boundaries.md` |

## Operating principles

- Phase-based development is mandatory.
- Docs come before implementation for architecture-impacting work.
- AI agents must inspect the current repo state before changing files.
- Feature gates default to off for production write capability.
- Preview-only behavior is not production-ready real write behavior.
- Real production writes require a dedicated Release Gate and explicit user approval.
- This project is commit-only by default; do not push unless the user explicitly asks.

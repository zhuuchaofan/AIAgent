# Phase Management Skill

## When to use

Use this when classifying a request, answering project status, planning next steps, starting implementation, reviewing docs, or deciding whether work belongs to Release Gate.

## Goal

Keep LifeOS work aligned with the authoritative Phase model and prevent agents from drifting into future scope or production enablement.

## Inputs

- `docs/skills/lifeos-phase-assessment.md`
- `_shared/phase-map.md`
- User request.
- Relevant Phase docs.
- Current diff, if work is already in progress.

## Process

1. Read or rely on the authoritative Phase assessment skill for current status.
2. Map the request to a Phase, stabilization concern, or Release Gate.
3. Identify non-goals and blocked areas.
4. If the request crosses Phases, split it into safe current work and gated follow-up.
5. State the Phase classification before making significant changes.

## Red lines

- Do not redefine Phase names.
- Do not call Release Gate a development Phase.
- Do not implement Phase 6 durable Memory writes while only approved for design or preview work.
- Do not mark canary, production enablement, or rollout complete without execution evidence.
- Do not treat docs describing a future plan as proof of implementation.

## Done criteria

- The request has a clear Phase or Release Gate classification.
- Scope and non-goals are explicit.
- Any implementation matches the allowed Phase boundary.
- Future or gated work is documented as future or gated.

## Checklist

- [ ] Phase source checked.
- [ ] Request mapped to Phase or Release Gate.
- [ ] Current allowed scope identified.
- [ ] Non-goals stated when needed.
- [ ] No design-only work is labeled implemented.
- [ ] No production enablement is performed without approval.

## Related skills

- `_shared/phase-map.md`
- `core/project-overview.md`
- `core/architecture-first.md`
- `core/docs-sync.md`
- `core/preview-confirm-write.md`

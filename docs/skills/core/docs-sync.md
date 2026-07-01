# Docs Sync Skill

## When to use

Use this when code changes behavior, contracts, phase status, tests, deployment results, safety posture, or user-visible flows.

## Goal

Keep LifeOS docs and code moving together so future agents do not rely on stale assumptions or historical chat context.

## Inputs

- Current diff.
- Relevant Phase docs.
- API, schema, architecture, test, smoke, or deployment docs.
- User request and final implemented behavior.

## Process

1. Determine whether the change affects architecture, API contract, Firestore schema, frontend flow, tests, deployment, or Release Gate status.
2. Update the smallest relevant doc when behavior changes.
3. Clearly label design, implementation, preview-only validation, deployment result, and Release Gate status.
4. Avoid rewriting unrelated historical docs.
5. If docs are intentionally not updated, state why.

## Red lines

- Do not let code behavior diverge from current docs.
- Do not claim a design is implemented.
- Do not claim preview-only validation proves production real-write readiness.
- Do not edit unrelated docs for cleanup during feature work.
- Do not mark Release Gate complete without evidence and approval.

## Done criteria

- Relevant docs are updated or a no-docs-needed reason is recorded.
- Phase status and safety status remain accurate.
- Future agents can understand the change from docs plus code.

## Checklist

- [ ] Architecture docs impact checked.
- [ ] API contract docs impact checked.
- [ ] Firestore schema docs impact checked.
- [ ] Testing / smoke docs impact checked.
- [ ] Deployment / Release Gate docs impact checked.
- [ ] Design vs implementation wording checked.

## Related skills

- `core/architecture-first.md`
- `core/phase-management.md`
- `_shared/common-checklists.md`
- `development/commit-readiness.md`

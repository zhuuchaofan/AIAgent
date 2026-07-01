# Phase Closeout Flow

## When to use

Use this when closing a Phase, subphase, readiness gate, stabilization pass, preview-only deployment result, or implementation milestone.

## Goal

Provide an ordered flow for closeout work without redefining the detailed rules from the underlying Skills.

## Inputs

- User request.
- Current Phase docs.
- Current diff and commits.
- Test, smoke, deployment, or review evidence.
- Release Gate status, if relevant.

## Process

1. Use `core/phase-management.md` to confirm the closeout scope.
2. Use `core/safety-boundaries.md` to separate development closeout from Release Gate actions.
3. Use `core/docs-sync.md` to identify required closeout docs or result updates.
4. Use `development/testing.md` to collect validation evidence.
5. Use `development/commit-readiness.md` if committing closeout docs or code.
6. Final response must state completed work, remaining blockers, validation, safety status, and commit hash when committed.

## Red lines

- Do not mark a Phase complete from design docs alone.
- Do not fold real-write canary into Phase closeout.
- Do not mark preview-only smoke as real-write production readiness.
- Do not deploy or enable flags unless explicitly requested and approved.

## Done criteria

- Closeout scope is classified correctly.
- Evidence supports the closeout claim.
- Remaining blockers are explicit.
- Release Gate status is separate from Phase status.

## Checklist

- [ ] Phase closeout scope confirmed.
- [ ] Evidence gathered.
- [ ] Docs updated or no-update reason recorded.
- [ ] Tests / smoke results recorded.
- [ ] Release Gate boundary preserved.
- [ ] Commit created only after commit-readiness conditions are met.

## Related skills

- `core/phase-management.md`
- `core/safety-boundaries.md`
- `core/docs-sync.md`
- `development/testing.md`
- `development/commit-readiness.md`

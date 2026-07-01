# Testing Skill

## When to use

Use this before finalizing code or docs that affect behavior, contracts, safety boundaries, deployment, Agent actions, RAG, Memory, or frontend flows.

## Goal

Apply validation proportional to risk while avoiding accidental production writes or deployments.

## Inputs

- Current diff.
- Changed files.
- Relevant test projects and scripts.
- Current Phase and safety boundary.
- User approval for any smoke or production-adjacent command.

## Process

1. Classify test scope: docs-only, backend, frontend, integration, smoke, deployment, or Release Gate.
2. Prefer targeted tests first, then broader tests when risk warrants.
3. For docs-only changes, run textual checks or review content; full build is optional unless requested.
4. Do not run deployment or real-write smoke as ordinary tests.
5. Report commands run, results, and skipped checks.

## Common commands

- Backend: `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
- Frontend lint: `npm run lint --prefix life-agent-web`
- Frontend build: `npm run build --prefix life-agent-web`
- Diff check: `git diff --check`

## Red lines

- Do not run real-write smoke without Release Gate approval.
- Do not treat skipped tests as passed.
- Do not deploy as part of testing unless explicitly asked.
- Do not write production Firestore / Storage data during validation.
- Do not hide test failures caused by the current change.

## Done criteria

- Validation matches the risk of the change.
- Test output or skipped-test rationale is recorded.
- No unapproved production action occurred.

## Checklist

- [ ] Changed file type classified.
- [ ] Targeted validation selected.
- [ ] Safety of validation command checked.
- [ ] Results recorded.
- [ ] Skipped checks explained.
- [ ] Failures investigated if caused by current change.

## Related skills

- `_shared/common-checklists.md`
- `core/safety-boundaries.md`
- `core/docs-sync.md`
- `development/commit-readiness.md`

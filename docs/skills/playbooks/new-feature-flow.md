# New Feature Flow

## When to use

Use this when starting a new LifeOS feature, behavior change, API change, UI flow, Agent capability, RAG capability, Memory capability, or data model change.

## Goal

Provide the default ordered workflow for safe feature development without repeating every rule from the component Skills.

## Inputs

- User request.
- Current Phase and non-goals.
- Existing docs and code.
- Safety and Release Gate constraints.
- Expected validation path.

## Process

1. Use `core/project-overview.md` if project context is unclear.
2. Use `core/phase-management.md` to classify the feature.
3. Use `core/architecture-first.md` before implementation when the feature affects architecture, data, Agent behavior, or writes.
4. Use `core/safety-boundaries.md` for auth, production, Firestore, Storage, Cloud Run, or write risks.
5. Use `core/preview-confirm-write.md` for any mutating Agent or Memory behavior.
6. Implement only within approved scope.
7. Use `core/docs-sync.md` to keep docs and code aligned.
8. Use `development/testing.md` for validation.
9. Use `development/commit-readiness.md` when committing.

## Red lines

- Do not implement before inspecting existing docs and code.
- Do not skip architecture review for cross-module or write-related work.
- Do not cross into Release Gate actions as part of normal feature work.
- Do not push by default.

## Done criteria

- Feature scope is mapped to the correct Phase.
- Architecture and safety checks are complete.
- Docs and code are consistent.
- Validation is recorded.
- Commit is local only when requested or appropriate.

## Checklist

- [ ] Phase classified.
- [ ] Existing docs/code inspected.
- [ ] Architecture need decided.
- [ ] Safety boundaries checked.
- [ ] Preview/confirm/write checked if relevant.
- [ ] Docs sync checked.
- [ ] Validation completed or explained.
- [ ] Commit readiness checked if committing.

## Related skills

- `core/project-overview.md`
- `core/phase-management.md`
- `core/architecture-first.md`
- `core/safety-boundaries.md`
- `core/docs-sync.md`
- `core/preview-confirm-write.md`
- `development/testing.md`
- `development/commit-readiness.md`

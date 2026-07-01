# Architecture First Skill

## When to use

Use this before implementing cross-module behavior, new API contracts, data model changes, Agent tools, Memory behavior, write paths, Firestore schema changes, or production-impacting features.

## Goal

Force design clarity before code so LifeOS changes preserve Phase scope, safety boundaries, tenant isolation, and docs/code alignment.

## Inputs

- User request.
- Current Phase and non-goals.
- Existing architecture docs.
- Relevant source files.
- Known safety constraints and Release Gate status.

## Process

1. Inspect existing docs and code before proposing implementation.
2. Define the target behavior, non-goals, data model, state transitions, auth boundary, failure behavior, and tests.
3. Decide whether docs must be written or updated before code.
4. Keep implementation incremental and aligned with existing service/repository patterns.
5. Do not proceed from design to code if the user only asked for analysis or design.

## Red lines

- Do not implement architecture-impacting behavior without first understanding current architecture.
- Do not add a new write path without preview/confirm/write and feature gate analysis.
- Do not change Firestore physical paths casually.
- Do not introduce production behavior that docs have not described.
- Do not label architecture design as completed implementation.

## Done criteria

- Architecture impact is documented or explicitly judged as not needed.
- Data ownership, auth, failure modes, and testing are clear.
- Implementation stays within the approved Phase.
- Docs and code claims are consistent.

## Checklist

- [ ] Existing docs reviewed.
- [ ] Existing code paths reviewed.
- [ ] Scope and non-goals defined.
- [ ] Data model and auth boundary checked.
- [ ] Preview/confirm/write impact checked.
- [ ] Test strategy defined.
- [ ] Docs update need decided.

## Related skills

- `core/phase-management.md`
- `core/docs-sync.md`
- `core/preview-confirm-write.md`
- `_shared/safety-red-lines.md`

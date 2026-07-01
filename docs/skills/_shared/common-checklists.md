# Common Checklists

## When to use

Use this as a shared checklist source for feature work, docs updates, testing, review, commit readiness, and Phase closeout.

## Goal

Reduce repeated coordination by giving agents stable checks that apply across LifeOS tasks.

## Inputs

- User request.
- Current Phase scope.
- Current diff.
- Relevant source files and docs.
- Test or smoke output.

## Process

1. Select the checklist that matches the task.
2. Apply Phase and safety checks first.
3. Apply docs, testing, and commit checks before final handoff.
4. Report skipped checks with the reason.

## Feature checklist

- [ ] Current Phase and non-goals are identified.
- [ ] Existing code and docs are inspected before edits.
- [ ] Architecture impact is assessed before implementation.
- [ ] Preview / confirm / write behavior is classified.
- [ ] Feature gates remain off unless explicitly approved.
- [ ] Tests or smoke checks match the risk level.
- [ ] Docs are updated when behavior or contracts change.

## Documentation checklist

- [ ] The doc states whether content is design, implementation, deployment result, or Release Gate.
- [ ] The doc does not claim future work is already implemented.
- [ ] Phase naming matches the shared Phase map.
- [ ] Production safety status is explicit.
- [ ] Follow-up work and blockers are separated from done criteria.

## Review checklist

- [ ] User identity is backend-derived.
- [ ] Firestore paths stay under `users/{userId}/...`.
- [ ] Write paths are gated and confirmation-aware.
- [ ] Preview-only responses cannot perform durable writes.
- [ ] Tests cover behavior, failure, and boundary cases.
- [ ] No unrelated files or generated noise are included.

## Commit checklist

- [ ] `git status` reviewed.
- [ ] `git diff --stat` reviewed.
- [ ] `git diff` reviewed.
- [ ] Tests or skipped-test reason recorded.
- [ ] `.claude/agents/` changes are not staged.
- [ ] No push is performed by default.

## Red lines

- Do not use checklists to bypass specific Skill rules.
- Do not mark skipped checks as passed.
- Do not include unrelated user changes in a commit.
- Do not use a Phase closeout checklist to approve Release Gate actions.

## Done criteria

- The relevant checklist is applied.
- Skipped checks are explained.
- The final response clearly states validation and safety status.

## Checklist

- [ ] Correct checklist selected.
- [ ] Phase and safety checks completed first.
- [ ] Testing or skip reason captured.
- [ ] Commit readiness checked when committing.

## Related skills

- `_shared/safety-red-lines.md`
- `_shared/phase-map.md`
- `development/testing.md`
- `development/commit-readiness.md`

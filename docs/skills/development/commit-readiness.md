# Commit Readiness Skill

## When to use

Use this whenever preparing a local commit or when the user asks to finish a task that should be committed.

## Goal

Ensure commits are scoped, reviewed, tested, and consistent with LifeOS rules. This project is commit-only by default: completing implementation work means creating a local commit when the worktree scope is clean. Do not push unless the user explicitly asks.

## Inputs

- `git status`
- `git diff --stat`
- `git diff`
- Test results or skipped-test rationale.
- User-provided commit instructions.

## Process

1. Review `git status` for tracked, untracked, and unrelated changes.
2. Review `git diff --stat` for scope.
3. Review `git diff` for actual content, safety, docs, and formatting.
4. Confirm tests or validation results are available.
5. Exclude unrelated files and never stage `.claude/agents/`.
6. Commit locally when the user asked to complete implementation work, the changed scope is clean, and no unrelated changes are present.
7. Do not commit when the user only asked for analysis, review, design, or no file changes.
8. Do not commit when the worktree contains unrelated changes that cannot be safely separated.
9. If the user did not provide a commit message, generate a clear Chinese commit message following project conventions.
10. After commit, output commit hash and current `git status`.

## Red lines

- Do not push by default.
- Do not include `.claude/agents/` changes in Git commits.
- Do not commit unrelated user changes.
- Do not commit without reviewing diff content.
- Do not commit analysis-only, review-only, design-only, or no-file-change tasks.
- Do not hide missing tests or skipped validation.
- Do not rewrite history unless explicitly requested.

## Done criteria

- Commit contains only intended changes.
- Commit message matches the task or user request.
- Commit hash is reported.
- Post-commit `git status` is reported.
- No push was performed unless explicitly requested.

## Checklist

- [ ] `git status` reviewed.
- [ ] `git diff --stat` reviewed.
- [ ] `git diff` reviewed.
- [ ] Tests or validation result recorded.
- [ ] `.claude/agents/` excluded.
- [ ] Only intended files staged.
- [ ] Task is not analysis-only, review-only, design-only, or no-file-change.
- [ ] Worktree contains no unrelated changes.
- [ ] Local commit created.
- [ ] Commit hash reported.
- [ ] Post-commit `git status` reported.
- [ ] No push performed.

## Related skills

- `_shared/common-checklists.md`
- `_shared/safety-red-lines.md`
- `development/testing.md`
- `core/docs-sync.md`

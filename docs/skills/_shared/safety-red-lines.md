# Safety Red Lines

## When to use

Use this before any change involving production behavior, Cloud Run, Firestore, Cloud Storage, Firebase Auth, Agent writes, Memory writes, feature gates, deployment, smoke tests, or commit preparation.

## Goal

Prevent AI agents from accidentally changing production behavior, enabling writes, corrupting tenant isolation, or overstating preview-only results.

## Inputs

- User request and explicit approvals.
- `git status` and current diff.
- Relevant Phase and Release Gate docs.
- Cloud Run / Firestore / Auth related files, if touched.

## Process

1. Classify the task as design, local implementation, preview-only validation, deployment, or Release Gate.
2. Identify whether the task can cause production writes or production configuration changes.
3. Stop and request explicit user approval for any Release Gate or production write action.
4. Verify `userId` is always supplied by backend-authenticated context.
5. Record the exact safety conclusion in final output.

## Red lines

- Do not modify Cloud Run environment variables without explicit user approval.
- Do not enable real write flags without explicit Release Gate approval.
- Do not perform real writes to production Firestore or Cloud Storage without explicit approval.
- Do not trust `userId` from frontend state, LLM output, tool input, or request body.
- Do not describe preview-only results as production-ready real write behavior.
- Do not deploy, change traffic, or modify Firebase project configuration unless asked.
- Do not change Firestore Rules or storage rules unless the task explicitly targets them.
- Do not run real-write smoke commands unless the Release Gate is approved.

## Done criteria

- The work stays within the approved safety scope.
- Production configuration and write behavior are unchanged unless explicitly approved.
- Identity and tenant isolation are preserved.
- Any preview-only result is labeled preview-only.

## Checklist

- [ ] No unapproved Cloud Run env change.
- [ ] No unapproved write flag enablement.
- [ ] No unapproved production Firestore / Storage write.
- [ ] No trusted `userId` from frontend, LLM, or request payload.
- [ ] No preview-only result described as production-ready.
- [ ] Release Gate work is separated from development work.
- [ ] Final response states whether production behavior changed.

## Related skills

- `_shared/phase-map.md`
- `core/safety-boundaries.md`
- `core/preview-confirm-write.md`
- `development/commit-readiness.md`
- `docs/skills/cloud-run-deploy.md`

# Safety Boundaries Skill

`docs/skills/_shared/safety-red-lines.md` is the source of truth for LifeOS safety red lines. This Skill applies those red lines to concrete task execution and must not weaken or redefine them.

## When to use

Use this before any work touching Agent actions, write paths, Firestore, Cloud Storage, Firebase Auth, Cloud Run, feature flags, deployment, smoke tests, or production claims.

## Goal

Apply LifeOS safety rules at task level so AI agents do not cross from local development or preview-only behavior into production writes.

## Inputs

- `_shared/safety-red-lines.md`
- User approvals.
- Current diff.
- Relevant service, repository, config, deployment, and docs files.

## Process

1. Identify whether the work can affect production config, data, auth, or write behavior.
2. Check whether the user explicitly approved any high-risk action.
3. Ensure all write-like operations use backend-controlled identity and confirmation boundaries.
4. Keep feature gates disabled by default.
5. Capture safety status in the final response.

## Red lines

- Do not modify Cloud Run env without explicit approval.
- Do not enable `ENABLE_AGENT_WRITE_TOOLS`, `ENABLE_CREATE_LIFE_EVENT_TOOL`, or equivalent real-write flags without Release Gate approval.
- Do not write to production Firestore or Cloud Storage without explicit approval.
- Do not trust `userId` from frontend, LLM, tool input, or request payload.
- Do not say preview-only is production-ready.
- Do not push by default.

## Done criteria

- No unapproved production-impacting change occurred.
- Risky behavior is gated, preview-only, or explicitly approved.
- Identity handling is backend-owned.
- Final output states whether production behavior changed.

## Checklist

- [ ] Production config risk checked.
- [ ] Production data write risk checked.
- [ ] Feature gate state remains safe.
- [ ] Trusted `userId` source verified.
- [ ] Preview-only language is accurate.
- [ ] Release Gate approval required for real writes.

## Related skills

- `_shared/safety-red-lines.md`
- `core/preview-confirm-write.md`
- `development/testing.md`
- `development/commit-readiness.md`

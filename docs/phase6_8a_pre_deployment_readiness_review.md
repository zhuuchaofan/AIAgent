# Phase 6.8A Pre-deployment Readiness Review

Date: 2026-07-02

## Scope

This document records a pre-deployment readiness review for Phase 6.8A
preview-only API deployment smoke.

This review does not:

- Deploy.
- Push.
- Modify code.
- Modify Cloud Run environment variables.
- Modify Firestore Rules.
- Modify MCP.
- Enable durable Memory write.
- Connect a real Firestore Memory runtime.
- Enter the durable Memory write Release Gate.

## Current State

Committed Phase 6.8A artifacts:

- Phase 6.8A implementation result docs are committed.
- Phase 6.8A local verification plan is committed.
- Phase 6.8A local verification result is committed.
- Phase 6.8A preview-only API deployment smoke plan is committed.

Related Phase 6.7A artifact:

- Phase 6.7A local verification result exists:
  `docs/phase6_7a_post_implementation_local_verification_result.md`

Latest local verification:

- `dotnet test`: 323 passed, 0 failed, 0 skipped.
- Default-off behavior is verified.
- Preview-only proposal behavior is verified.
- No-write behavior is verified.
- RAG / life_event / reminder / read-only retrieval regression is covered.
- Real Firestore Memory runtime is not connected.
- `users/{userId}/memories` is not written.
- Durable Memory write is not enabled.
- Deployment has not been performed.
- Push has not been performed.

## Git State Review

Command:

```bash
git status --branch --short
```

Result:

```text
## main...origin/main [ahead 7]
```

Command:

```bash
git status --short
```

Result:

```text
clean
```

Command:

```bash
git log --oneline --decorate -10
```

Result:

```text
5bfbc66 (HEAD -> main) docs: 增加 Phase 6.8A preview-only deployment smoke plan
f52c272 docs: 记录 Phase 6.8A local verification result
78ac7b6 docs: 增加 Phase 6.8A local verification plan
08b6c36 docs: 记录 Phase 6.8A memory proposal runtime implementation result
77910ea feat: add guarded memory preview proposal runtime
6553d91 docs: 增加 Phase 6.8A memory proposal runtime implementation plan
75e3f7d docs: 记录 Phase 6.7A preview-only deployment smoke result
8201d20 (origin/main, origin/HEAD) docs: 增加 Phase 6.7A preview-only API deployment smoke plan
72e4dcf docs: 记录 Phase 6.7A local verification result
02bfea9 docs: 增加 Phase 6.7A local verification plan
```

Git review result:

- Current branch is ahead of `origin/main` by 7 commits.
- Working tree was clean before creating this review document.
- Latest commit contains the Phase 6.8A preview-only deployment smoke plan.
- No uncommitted changes existed before this review document.

## Local Test Review

Command:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

Result:

- Passed: 323
- Failed: 0
- Skipped: 0

Known warnings:

- Existing nullable warnings remain in `LifeAgent.Api/Services/RagChatService.cs`.

No new test failures were observed.

## Diff / Working Tree Review

Command:

```bash
git diff --check
```

Result:

```text
passed
```

Command:

```bash
git diff --stat
```

Result before this review document:

```text
clean
```

Command:

```bash
git status --short
```

Result before this review document:

```text
clean
```

Expected status after this document is created:

- Only `docs/phase6_8a_pre_deployment_readiness_review.md` should be
  untracked.

## No-write Boundary Review

| Check | Result |
| --- | --- |
| Real Firestore Memory runtime connected | no |
| Writes `users/{userId}/memories` | no |
| Writes `life_events` | no |
| Adds production API endpoint | no |
| Enables durable Memory write | no |
| Modifies Cloud Run env | no |
| Modifies Firestore Rules | no |
| Modifies MCP | no |
| Deploys | no |
| Pushes | no |

## Cloud Run Env Precheck Plan

No deployment was executed during this readiness review.

Cloud Run env describe was not executed in this review and remains a pending
pre-deployment check.

Before any user-approved deployment smoke, a read-only Cloud Run env describe
should confirm:

- `ENABLE_AGENT_WRITE_TOOLS` is not enabled.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is not enabled.
- Durable Memory write flags are not enabled.
- `USE_MOCK_AUTH=false` or unset.
- `USE_MOCK_LLM=false` or unset.
- No new Memory write flags are added.
- Cloud Run env is not modified as part of preview-only smoke.

Suggested read-only command for the future smoke window:

```bash
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="json(spec.template.spec.containers[0].env)"
```

## FIREBASE_ID_TOKEN Readiness

Command:

```bash
echo "$FIREBASE_ID_TOKEN" | cut -c1-40
```

Result:

- Token exists in current shell: no.
- Full token recorded: no.

Impact:

- Authenticated smoke will SKIP or be BLOCKED until a valid token is supplied.
- Do not use a fake token.
- Do not enable mock auth.

## Deployment Approval Gate

Even if readiness review passes, deployment must not start automatically.

Executing preview-only API deployment smoke requires separate user approval.

## Final Decision

Decision: `PARTIAL READY - TOKEN OR ENV CHECK PENDING`

Reason:

- Local tests passed.
- Git working tree was clean before this review document.
- Phase 6.8A smoke plan is committed.
- No-write boundaries remain intact.
- `FIREBASE_ID_TOKEN` is not present in the current shell.
- Cloud Run env describe remains a pending pre-deployment check.

No deployment was executed. Durable Memory write remains disabled. Real
Firestore Memory runtime remains disconnected. User approval is required before
deployment.

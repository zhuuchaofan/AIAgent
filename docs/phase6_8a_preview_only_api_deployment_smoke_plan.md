# Phase 6.8A Preview-only API Deployment Smoke Plan

Date: 2026-07-02

## Scope

This document plans a future preview-only API deployment smoke for Phase 6.8A
Memory proposal runtime implementation.

This task only writes the smoke plan.

It does not:

- Execute deployment.
- Modify Cloud Run environment variables.
- Modify Firestore Rules.
- Modify MCP.
- Enable durable Memory write.
- Enter the durable Memory write Release Gate.
- Push commits.
- Start the next implementation stage.

## Current State

Current Phase 6.8A status:

- Phase 6.8A implementation result docs are committed.
- Phase 6.8A local verification plan is committed.
- Phase 6.8A local verification result is committed.
- `dotnet test`: 323 passed, 0 failed, 0 skipped.
- Default-off behavior is verified.
- Preview-only proposal behavior is verified.
- No-write behavior is verified.
- RAG / life_event / reminder / read-only retrieval regression is covered.
- Existing nullable warnings remain in `LifeAgent.Api/Services/RagChatService.cs`.
- Real Firestore Memory runtime is not connected.
- Durable Memory write is not enabled.
- Deployment has not been performed for Phase 6.8A.
- Push has not been performed.

Phase 6.7A local verification result:

- Found: `docs/phase6_7a_post_implementation_local_verification_result.md`

This document does not modify or supplement Phase 6.7A documents.

## Deployment Preconditions

Before any Phase 6.8A preview-only API deployment smoke is approved, all of
these must be true:

- `git status` is clean.
- Latest local tests passed.
- Phase 6.8A local verification result is committed.
- Cloud Run write flags remain disabled.
- `ENABLE_AGENT_WRITE_TOOLS` is not enabled.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is not enabled.
- Durable Memory write is not enabled.
- No real Firestore Memory repository is added.
- No `users/{userId}/memories` write path is added.
- No production Memory API endpoint is added.
- Firestore Rules are unchanged.
- MCP is unchanged.
- User explicitly approves deployment.

## Deployment Boundary

Future deployment smoke boundaries:

- API deployment only.
- Do not deploy Web unless separately approved.
- Do not change Cloud Run env.
- Do not set proposal runtime flags unless separately approved for a narrow
  preview-only canary.
- Do not enable Agent write flags.
- Do not enable durable Memory write.
- Do not enable mock auth.
- Do not enable mock LLM.
- Do not modify Firestore Rules.
- Do not modify MCP.
- Do not push.

The standard API deployment command, if separately approved later, should follow
`docs/skills/cloud-run-deploy.md` and use the API service source directory:

```bash
gcloud run deploy life-agent-api \
  --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated
```

This plan does not execute that command.

## Pre-deployment Checks

Run these before any separately approved deployment:

```bash
git status --short
git diff --stat
git diff --check
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

For API-only deployment of this backend change, Web deployment is not required.
If Web deployment is separately requested, follow the Web deployment checks in
`docs/skills/cloud-run-deploy.md`.

## Cloud Run Env Verification Plan

Before deployment smoke, inspect the current API env and verify:

- `USE_MOCK_AUTH` is not `true`.
- `USE_MOCK_LLM` is not `true`.
- `ENABLE_AGENT_WRITE_TOOLS` is unset or `false`.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is unset or `false`.
- `ENABLE_MEMORY_PROPOSAL_RUNTIME` is unset or `false` unless explicitly
  approved for preview-only canary smoke.
- `ENABLE_MEMORY_PROPOSAL_GUARD` is unset or `false` unless explicitly approved
  for preview-only canary smoke.
- No durable Memory write flag is enabled.

Suggested read-only command:

```bash
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format="json(spec.template.spec.containers[0].env)"
```

## Smoke Commands Plan

These commands are planned for future smoke only. Do not run them as part of
this planning task.

### API health

```bash
curl -s "$API_BASE_URL/health"
```

Expected: healthy response.

### Agent life_event preview-only smoke

```bash
API_BASE_URL="$API_BASE_URL" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-agent-life-event-write.mjs
```

Expected:

- PASS API health.
- PASS Agent proposes life_event action.
- Confirm remains preview-only unless write flags are separately enabled.
- `wroteData=false` for preview-only path.

### RAG / Agent Preview smoke

```bash
API_BASE_URL="$API_BASE_URL" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-rag-e2e.mjs
```

Expected:

- PASS API health.
- PASS API endpoint responds.
- PASS Web endpoint reachable if included in the smoke script.
- PASS Agent Preview read-only checks.
- No mutating RAG flow unless separately approved.

### Memory proposal preview-only spot check

If a dedicated smoke script exists later, it should verify:

- Explicit memory intent creates only `save_memory_preview`.
- Default-off serialized payload contains no guard fields.
- With proposal flags off, behavior matches current production contract.
- If a separately approved canary enables proposal flags, `allowed`,
  `review_required`, and `blocked` remain preview-only / no-write.

If no dedicated script exists, record this as a manual/API smoke follow-up rather
than inventing a new test system during deployment.

## FIREBASE_ID_TOKEN Handling

Authenticated smoke requires a real Firebase ID token.

Rules:

- Do not use fake tokens.
- Do not enable mock auth as a substitute.
- Do not print or store the full token.
- If `FIREBASE_ID_TOKEN` is missing, authenticated smoke is incomplete and must
  be recorded as skipped or blocked.

## Expected Results

Expected preview-only smoke result:

- `/health`: PASS.
- `smoke-agent-life-event-write`: PASS.
- `smoke-rag-e2e`: PASS.
- Default-off `save_memory_preview` contract remains unchanged.
- No guard fields appear in default-off serialized memory preview payload.
- Any guarded proposal checks remain preview-only.
- Confirming `save_memory_preview` returns `previewOnly=true`.
- Confirming `save_memory_preview` returns `wroteData=false`.
- `createdResourceId` remains null / empty for memory preview confirm.
- No `users/{userId}/memories` write.
- No `life_events` write from Memory proposal runtime.
- No extraction trigger.
- No durable Memory write.
- Cloud Run write flags remain disabled.

## No-write Checklist

During smoke, verify:

- `users/{userId}/memories` write: false.
- `life_events` write from Memory proposal runtime: false.
- Durable Memory write: false.
- Background extraction: false.
- RAG/chat automatic memory proposal: false.
- Real Firestore Memory repository: absent.
- `save_memory_preview` confirm `wroteData`: false.
- Agent write flags: disabled.

## Stop Conditions

Stop immediately if any of these occur:

- `wroteData=true` for `save_memory_preview`.
- Unexpected `createdResourceId` for memory preview confirm.
- `users/{userId}/memories` is written.
- `life_events` is written by Memory proposal runtime.
- Extraction is triggered.
- RAG/chat/background automatically creates memory proposals.
- Default-off memory preview payload includes guard fields.
- Cloud Run env modification is required.
- Firestore Rules modification is required.
- MCP modification is required.
- Mock auth or mock LLM is required.
- Authenticated smoke lacks token but is forcibly bypassed.

## Result Document To Create Later

If the future preview-only API deployment smoke is actually executed, create:

```text
docs/phase6_8a_preview_only_api_deployment_smoke_result.md
```

Do not create that result document until deployment smoke is actually executed.
This task creates only the plan.

## Final Conclusion

Phase 6.8A preview-only API deployment smoke is planned but not executed.

This plan does not deploy. It does not modify Cloud Run env, Firestore Rules, or
MCP. It does not enable durable Memory write. It does not enter the Release
Gate.

Deployment requires separate user approval.

# Phase 6.7A Preview-only API Deployment Smoke Plan

Date: 2026-07-01

## Scope

This document plans a future preview-only API deployment smoke for Phase 6.7A
Read-only MemoryContextProvider runtime code.

This task does not:

- Execute deployment.
- Modify Cloud Run environment variables.
- Modify Firestore Rules.
- Enable durable memory write.
- Enter Phase 6.8 implementation.
- Push commits.

## Current State

- Phase 6.7A implementation is complete and committed.
- Implementation commit: `db191900d661caba011a518553ac44cf6fe4ae60`
- Phase 6.7A result docs are committed.
- Phase 6.7A local verification plan is committed.
- Phase 6.7A local verification has passed.
- `dotnet test`: 319 passed, 0 failed, 0 skipped.
- Read-only `MemoryContextProvider` is wired.
- Default flags are off.
- With no env configured, runtime uses no-op behavior.
- With flags off, response payload contract remains unchanged.
- With flags on, runtime uses fake/in-memory read-only provider only.
- Real Firestore memory repository is not connected.
- No production API endpoint was added.
- No `users/{userId}/memories` write path was added.
- No `life_events` write path was added.
- Retrieval does not create pending actions.
- Retrieval does not trigger extraction.
- Retrieval does not automatically create `save_memory_preview`.
- Durable memory write is not enabled.
- Deployment has not been performed.
- Push has not been performed.

## Deployment Goal

The future preview-only API deployment smoke should verify that after deploying
the Phase 6.7A runtime code:

- API `/health` is healthy.
- RAG smoke works.
- `life_event` preview-only smoke works.
- `save_memory_preview` preview-only behavior remains unchanged.
- Default-off memory context does not change online response contracts.
- No `wroteData=true` is observed.
- No `users/{userId}/memories` document is created.
- No `life_events` document is created by retrieval.
- Durable memory write remains disabled.

## Pre-deployment Checks

Before any deployment smoke is approved, verify:

- `git status` is clean.
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj` has passed.
- Local verification result is committed.
- Cloud Run write flags remain off.
- `ENABLE_MEMORY_RETRIEVAL` is unset or `false`.
- `ENABLE_MEMORY_CONTEXT_IN_AGENT` is unset or `false`.
- `ENABLE_AGENT_WRITE_TOOLS` is unset or `false`.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is unset or `false`.
- `USE_MOCK_AUTH=false`.
- `USE_MOCK_LLM=false`.
- Firestore Rules are unchanged.
- MCP is unchanged.

## Deployment Boundary

Future deployment smoke boundaries:

- API deployment only.
- Do not deploy Web.
- Do not change Cloud Run env.
- Do not change feature flags.
- Do not enable mock auth.
- Do not enable mock LLM.
- Do not enable memory retrieval flags.
- Do not enable Agent write flags.
- Do not modify Firestore Rules.
- Do not modify MCP.
- Do not push.

## Smoke Commands Plan

The future smoke should include these checks. Do not run them as part of this
plan document.

### API health

```bash
curl -s "$API_BASE_URL/health"
```

Expected: `healthy`.

### `smoke-agent-life-event-write`

```bash
API_BASE_URL="$API_BASE_URL" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-agent-life-event-write.mjs
```

### `smoke-rag-e2e`

```bash
API_BASE_URL="$API_BASE_URL" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-rag-e2e.mjs
```

FIREBASE token requirements:

- If `FIREBASE_ID_TOKEN` is missing, authenticated smoke must be SKIPPED or
  BLOCKED.
- Do not use fake tokens.
- Do not enable mock auth as a substitute.
- Do not print or record the full token.

## Expected Results

Expected smoke result:

- `/health`: PASS.
- `smoke-agent-life-event-write`: PASS.
- Confirm returns `previewOnly=true`.
- Confirm returns `wroteData=false`.
- `createdResourceId` is empty or `null`.
- `smoke-rag-e2e`: PASS.
- No `users/{userId}/memories` write.
- No `life_events` write from retrieval.
- No durable memory write.
- No `memoryContext` diagnostics in default-off online response unless
  explicitly enabled.
- Cloud Run write flags remain off.

## Stop Conditions

Stop immediately if any of the following occur:

- `wroteData=true`.
- Unexpected `createdResourceId`.
- `users/{userId}/memories` is written.
- `life_events` is written by retrieval.
- Retrieval creates a pending action.
- Extraction is triggered.
- `save_memory_preview` is automatically created by retrieval.
- Cloud Run env modification is required.
- Firestore Rules modification is required.
- MCP modification is required.
- Mock auth or mock LLM is required.
- Authenticated smoke lacks token but is forcibly bypassed.

## Result Document To Create Later

If the future preview-only API deployment smoke is actually executed, create:

```text
docs/phase6_7a_preview_only_api_deployment_smoke_result.md
```

Do not create that result document until deployment smoke is actually executed.
This task creates only the plan.

## Final Conclusion

Phase 6.7A is locally verified.

Preview-only API deployment smoke is planned but not executed.

Deployment requires separate user approval.

Real Firestore memory runtime remains disconnected.

Durable memory write remains disabled.

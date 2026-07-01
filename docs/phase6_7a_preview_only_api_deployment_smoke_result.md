# Phase 6.7A Preview-only API Deployment Smoke Result

Date: 2026-07-01

## Scope

This document records the Phase 6.7A preview-only API deployment smoke result.

This was an API-only deployment and smoke attempt for the default-off read-only
MemoryContextProvider runtime code.

It does not mean:

- Durable Memory write is enabled.
- Real Firestore Memory runtime is connected.
- Phase 6.8 implementation has started.
- Web was deployed.

## Deployment Summary

Pre-deployment local checks:

- `git status --short`: clean
- `git diff --stat`: clean
- `git diff --check`: passed
- `npm run lint --prefix life-agent-web`: passed
- `npm run build --prefix life-agent-web`: passed
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`: passed
  - Passed: 319
  - Failed: 0
  - Skipped: 0
  - Existing nullable warnings in `LifeAgent.Api/Services/RagChatService.cs`

Deployment command:

```bash
gcloud run deploy life-agent-api \
  --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated
```

Deployment result:

- Service: `life-agent-api`
- Old revision: `life-agent-api-00037-r6m`
- New revision: `life-agent-api-00038-w9d`
- Traffic: 100% to `life-agent-api-00038-w9d`
- Service URL: `https://life-agent-api-151587524132.us-central1.run.app`
- Web deployed: no
- Push performed: no

## Cloud Run Env Verification

Post-deployment API env:

- `USE_MOCK_AUTH=false`
- `USE_MOCK_LLM=false`
- `ENABLE_MEMORY_RETRIEVAL`: not set
- `ENABLE_MEMORY_CONTEXT_IN_AGENT`: not set
- `ENABLE_AGENT_WRITE_TOOLS`: not set
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set

No Cloud Run env flags were passed in the deploy command.

## Smoke Commands

API health:

```bash
curl -s https://life-agent-api-151587524132.us-central1.run.app/health
```

Result:

```text
"healthy"
```

Agent life event smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
node scripts/smoke-agent-life-event-write.mjs
```

Result:

```text
PASS API /health returns healthy
SKIP Authenticated Agent flow: FIREBASE_ID_TOKEN is not set.
```

RAG smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
node scripts/smoke-rag-e2e.mjs
```

Result:

```text
PASS API /health returns healthy
PASS API endpoint responds
PASS Web endpoint is reachable
SKIP Authenticated RAG and Agent Preview flows: FIREBASE_ID_TOKEN is not set.
```

## Initial FIREBASE_ID_TOKEN Status

- `FIREBASE_ID_TOKEN` present in shell: no
- Full token recorded: no
- Fake token used: no
- Mock auth enabled: no
- Authenticated smoke status: skipped / incomplete

## Authenticated Follow-up Smoke

Execution time: 2026-07-01 20:44:46 CST

Token handling:

- `FIREBASE_ID_TOKEN` present: yes
- Full token recorded: no
- Fake token used: no
- Mock auth enabled: no

Agent life event smoke command:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-agent-life-event-write.mjs
```

Result:

```text
PASS API /health returns healthy
PASS Agent proposes life_event action
PASS Confirm action and verify expected write mode
PASS Repeat confirm and verify idempotency
SKIP Real write assertions: Set RUN_AGENT_WRITE_SMOKE=true and EXPECT_AGENT_WRITE_ENABLED=true to require wroteData=true.
```

RAG smoke command:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-rag-e2e.mjs
```

Result:

```text
PASS API /health returns healthy
PASS API endpoint responds
PASS Web endpoint is reachable
PASS Agent Preview lists documents
PASS Agent Preview proposes reminder confirmation
SKIP Authenticated upload/RAG/delete flow: RUN_MUTATING_SMOKE=true is required to create and delete a temporary test document.
```

Authenticated smoke status: complete.

## No-write Verification

| Check | Result |
| --- | --- |
| Real Firestore memory repository connected | no |
| Writes `users/{userId}/memories` | no; memory retrieval flags are off |
| Writes `life_events` from retrieval | no |
| Adds production API endpoint | no |
| Retrieval creates pending action | no |
| Triggers extraction | no |
| Automatically creates `save_memory_preview` | no |
| Enables durable memory write | no |
| Modifies Cloud Run env | no |
| Modifies Firestore Rules | no |
| Modifies MCP | no |
| Deploys Web | no |
| Pushes commits | no |

## Result

Preview-only API deployment completed.

Basic API health and unauthenticated API/Web reachability smoke passed.

Authenticated follow-up smoke passed for:

- `smoke-agent-life-event-write`
- `smoke-rag-e2e`

Phase 6.7A default-off memory flags remain disabled in Cloud Run. Durable Memory
write remains disabled. Real Firestore Memory runtime remains disconnected.

No further authenticated smoke follow-up is required for this Phase 6.7A
preview-only API deployment smoke.

# Phase 5.6 Preview-Only API Deployment Result

## Deployment Goal

Phase 5.6 deployed a new API revision containing the Agent `create_life_event` real-write code path while keeping production write flags disabled.

Expected production behavior remains preview-only:

- `ENABLE_AGENT_WRITE_TOOLS` is unset or false.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is unset or false.
- `/api/agent/confirm` must not create `life_event` documents.
- `previewOnly=true`.
- `wroteData=false`.

## Pre-Deployment Checks

Commands and results:

```text
git status --branch --short: main...origin/main [ahead 6]
git status --short: clean
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj: passed, 209 passed
npm run lint --prefix life-agent-web: passed
npm run build --prefix life-agent-web: passed
git diff --check: passed
```

Known warning:

- Existing nullable warnings remain in `LifeAgent.Api/Services/RagChatService.cs`.
- They were not introduced by Phase 5.6.

## Pre-Deployment Cloud Run Env

Pre-deployment API revision:

```text
life-agent-api-00033-gtt
```

Pre-deployment env:

```text
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set
ENABLE_CREATE_LIFE_EVENT_TOOL: not set
```

Traffic before deployment:

```text
life-agent-api-00033-gtt: 100%
```

## Deployment Result

Only the API was deployed.

New API revision:

```text
life-agent-api-00034-n6v
```

Traffic:

```text
life-agent-api-00034-n6v: 100%
```

Web revision did not change:

```text
life-agent-web-00018-bpq
```

## Post-Deployment Env

Post-deployment env remains safe:

```text
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set
ENABLE_CREATE_LIFE_EVENT_TOOL: not set
```

Because both Agent write flags are unset, `AgentWriteFeatureGate.CanCreateLifeEvent()` should remain false in production.

## Smoke Results

Health check:

```text
curl https://life-agent-api-151587524132.us-central1.run.app/health
healthy
```

Agent LifeEvent write smoke:

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

Authenticated flows were skipped because `FIREBASE_ID_TOKEN` was not set in the current shell.

## Safety Conclusion

Phase 5.6 did not enable real writes.

Observed safety state:

- No `wroteData=true` result appeared.
- No `life_event` was created.
- Real write smoke was not executed.
- `RUN_AGENT_WRITE_SMOKE=true` was not set.
- `EXPECT_AGENT_WRITE_ENABLED=true` was not set.
- Production remains preview-only.

## Known Gap

Authenticated preview-only smoke has not yet been executed.

After a valid `FIREBASE_ID_TOKEN` is available, run:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="..." \
node scripts/smoke-agent-life-event-write.mjs
```

Expected result with production flags still disabled:

- `proposedAction` is generated.
- Confirm returns `previewOnly=true`.
- Confirm returns `wroteData=false`.
- `createdResourceId` is empty.
- No `life_event` is created.

Do not set `RUN_AGENT_WRITE_SMOKE=true` or `EXPECT_AGENT_WRITE_ENABLED=true` for this preview-only verification.

## Final Conclusion

Phase 5.6 deployment succeeded.

The API now runs revision `life-agent-api-00034-n6v` with traffic at 100%, while production Agent writes remain disabled.

Current recommendation:

- Do not enable real writes yet.
- Next recommended phase: Phase 5.7 authenticated preview-only smoke.
- Do not proceed to real write enablement until authenticated preview-only behavior is verified.

# Phase 6.1 to 6.5 Post-Closeout Deployment Result

Execution time: 2026-06-30 23:19:35 CST

## Scope

This was a post-closeout preview-only API deployment smoke attempt. It was a standalone validation action and was not part of Phase 6.1 to 6.5 implementation.

## Closeout Commit

- Closeout commit hash: `309586b`
- Commit message: `docs: 增加 Phase 6.1 到 6.5 memory skeleton closeout`

## Pre-Deployment Git State

- Branch state after closeout commit: `main...origin/main [ahead 3]`
- Working tree before deployment: clean
- `git diff --check`: passed

## Pre-Deployment Test Result

Command:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

Result:

- Passed: 311
- Failed: 0
- Skipped: 0
- Total: 311
- Warnings observed: existing nullable warnings in `LifeAgent.Api/Services/RagChatService.cs`

## Pre-Deployment Cloud Run API Env

Service: `life-agent-api`

- `USE_MOCK_AUTH=false`
- `USE_MOCK_LLM=false`
- `ENABLE_AGENT_WRITE_TOOLS`: not set
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set
- Write flags: disabled

## Deployment

- Deployed service: `life-agent-api`
- Not deployed: `life-agent-web`
- Deployment command used the existing API-only Cloud Run path with `--source ./LifeAgent.Api`
- No Cloud Run env flags were passed or changed
- No write flags were enabled

## Revisions

- Old API revision: `life-agent-api-00036-jlh`
- New API revision: `life-agent-api-00037-r6m`
- API traffic: 100% to `life-agent-api-00037-r6m`
- Web revision before deployment: `life-agent-web-00018-bpq`
- Web revision after deployment: `life-agent-web-00018-bpq`
- Web revision changed: no

## Post-Deployment Cloud Run API Env

Service: `life-agent-api`

- `USE_MOCK_AUTH=false`
- `USE_MOCK_LLM=false`
- `ENABLE_AGENT_WRITE_TOOLS`: not set
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set
- Write flags: disabled

## FIREBASE_ID_TOKEN

- `FIREBASE_ID_TOKEN` present: no
- Full token recorded: no
- Authenticated smoke status: skipped

## Smoke Results

### `scripts/smoke-agent-life-event-write.mjs`

- Status: skipped
- Reason: `FIREBASE_ID_TOKEN` was missing
- Mock auth enabled: no
- Fake token used: no
- `RUN_AGENT_WRITE_SMOKE`: not set
- `EXPECT_AGENT_WRITE_ENABLED`: not set
- `wroteData=true` observed: no
- `previewOnly=false` observed: no
- `createdResourceId` observed: no
- `life_event` created: no evidence; authenticated smoke was not executed

### `scripts/smoke-rag-e2e.mjs`

- Status: skipped
- Reason: `FIREBASE_ID_TOKEN` was missing
- Mock auth enabled: no
- Fake token used: no
- Mutating smoke: not executed

## Safety Checks

- Real write enabled: no
- Cloud Run env changed: no intended env change; post-deployment env remained write-disabled
- Firestore Rules changed: no
- MCP changed: no
- Web deployed: no
- Entered later phase: no
- Memory store write: no
- `life_events` write from this smoke: no evidence; authenticated smoke was skipped

## Final Conclusion

API deployment completed, but authenticated post-closeout smoke was skipped because `FIREBASE_ID_TOKEN` was missing. Follow-up authenticated smoke is still required.

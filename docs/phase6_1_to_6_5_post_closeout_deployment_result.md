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

## Follow-Up Authenticated Smoke

Execution time: 2026-07-01 19:19:00 CST

Scope:

- This was a post-closeout authenticated smoke follow-up only.
- No new phase was entered.
- No implementation work was performed.
- No deployment was performed.
- No Cloud Run, Firestore Rules, or MCP configuration was changed.

Token handling:

- `FIREBASE_ID_TOKEN` present: yes
- Full token recorded: no
- Fake token used: no
- Mock auth enabled by this smoke: no

Commands:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-agent-life-event-write.mjs
```

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-rag-e2e.mjs
```

### Follow-Up `scripts/smoke-agent-life-event-write.mjs`

Result: passed

Observed output summary:

- `RUN_AGENT_WRITE_SMOKE=false`
- `EXPECT_AGENT_WRITE_ENABLED=false`
- `PASS API /health returns healthy`
- `PASS Agent proposes life_event action`
- `PASS Confirm action and verify expected write mode`
- `PASS Repeat confirm and verify idempotency`
- `SKIP Real write assertions: Set RUN_AGENT_WRITE_SMOKE=true and EXPECT_AGENT_WRITE_ENABLED=true to require wroteData=true.`

Validated behavior:

- Authenticated Agent flow passed.
- Agent life event proposal passed.
- Confirmation passed in preview-only mode.
- Repeated confirmation passed idempotently.
- Real write assertions were not enabled.
- `wroteData=true` observed: no
- `previewOnly=false` observed: no
- `createdResourceId` observed: no
- `life_event` created: no

### Follow-Up `scripts/smoke-rag-e2e.mjs`

Result: passed

Observed output summary:

- `PASS API /health returns healthy`
- `PASS API endpoint responds`
- `PASS Web endpoint is reachable`
- `PASS Agent Preview lists documents`
- `PASS Agent Preview proposes reminder confirmation`
- `SKIP Authenticated upload/RAG/delete flow: RUN_MUTATING_SMOKE=true is required to create and delete a temporary test document.`

Validated behavior:

- Authenticated Agent Preview document-list flow passed.
- Authenticated Agent Preview reminder confirmation flow passed.
- Mutating upload/RAG/delete smoke was not enabled.
- `RUN_MUTATING_SMOKE`: not set

### Follow-Up Safety Result

- Real write enabled: no
- Real write occurred: no
- `RUN_AGENT_WRITE_SMOKE`: not set
- `EXPECT_AGENT_WRITE_ENABLED`: not set
- `RUN_MUTATING_SMOKE`: not set
- Cloud Run env changed: no
- Firestore Rules changed: no
- MCP changed: no
- Business code changed: no
- Docs-only update after smoke: yes
- Memory skeleton remains local-only / fake-only / preview-only: yes
- Production Memory runtime wiring enabled: no

### Follow-Up Conclusion

Authenticated post-closeout smoke is complete. Both `smoke-agent-life-event-write` and `smoke-rag-e2e` passed under authenticated preview-only conditions, with no real write observed.

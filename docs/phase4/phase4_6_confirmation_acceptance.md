# Phase 4.6 Confirmation Acceptance

## Current Scope

Phase 4.6 is a stabilization and acceptance phase for Agent Preview confirmation flows. It does not add real write capability.

- Agent Preview is read-only for document/RAG tools.
- Agent Preview can return a `proposedAction` for write-like user intent.
- Users can confirm or cancel a pending preview action.
- Pending actions follow the lifecycle state machine: `created` -> `pending` -> `confirmed` / `cancelled` / `expired`.
- Confirmation is preview-only. It does not write reminders, life events, memories, or other persisted user data.

## Production Manual Acceptance

1. Open `https://life.zhuchaofan.com/`.
2. Log in with a real Firebase user.
3. Enter the `知识库问答 (RAG)` tab.
4. Expand `Agent Preview`.
5. Send `列出我的文档`.
6. Verify the response uses the read-only document listing flow.
7. Send `根据文档回答：这份文档主要讲了什么？`.
8. Verify the response uses the read-only RAG answer flow.
9. Send `明天提醒我观察黑猫`.
10. Verify a confirmation card appears with a proposed action.
11. Click confirm.
12. Verify the UI shows preview success / confirmed state and makes clear no data was written.
13. Send `明天提醒我观察黑猫` again.
14. Click cancel.
15. Verify the UI shows cancelled state and makes clear no data was written.
16. Verify no real reminder, life event, or memory was created.

## Automated Verification Commands

Run the full backend test suite:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

Run frontend lint and build:

```bash
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
```

Check whitespace and patch formatting:

```bash
git diff --check
```

Run the smoke script against local or configured endpoints:

```bash
node scripts/smoke-rag-e2e.mjs
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" node scripts/smoke-rag-e2e.mjs
```

Authenticated Agent Preview checks require a Firebase ID token:

```bash
FIREBASE_ID_TOKEN="..." API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" node scripts/smoke-rag-e2e.mjs
```

If `FIREBASE_ID_TOKEN` is missing, the authenticated Agent Preview checks must SKIP instead of failing. Do not hard-code tokens in the script.

The existing upload/RAG/delete smoke remains gated behind:

```bash
RUN_MUTATING_SMOKE=true
```

Do not enable `RUN_MUTATING_SMOKE` unless temporary document creation and cleanup are intended.

## Smoke Coverage

With `API_BASE_URL` set and no token, the script can automatically verify:

- API `/health`.
- API root does not return 5xx.
- Web root is reachable.
- Authenticated RAG and Agent Preview flows are skipped explicitly.

With `FIREBASE_ID_TOKEN` set, the script can also verify:

- `/api/agent/run` with `列出我的文档` returns a successful Agent response.
- The returned `toolCalls` include `list_documents`.
- `/api/agent/run` with `明天提醒我观察黑猫` returns `requiresConfirmation=true`.
- The response includes a `proposedAction`.
- `/api/agent/confirm` with `confirm` succeeds.
- The confirmation response is preview-only and reports `wroteData=false`.

The script does not verify real database writes because Phase 4.6 must not write user data.

## Known Limits

- The pending action store is in-memory and can be lost on Cloud Run restart.
- Multiple Cloud Run instances do not share pending actions.
- Real write tools are not supported in Phase 4.6.
- Before Phase 5 real writes, pending action state must move to Firestore or another durable store.
- Current Agent Preview does not use MCP.
- Current Agent Preview does not introduce multi-agent orchestration.

## Disable Agent Preview

Agent Preview can be disabled without changing the main RAG path:

1. Set the Cloud Run Web environment variable and build environment variable to `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW=false`.
2. Redeploy the Web service.
3. Or roll back the Web service to a previous revision where Agent Preview was not visible.

Do not change API write behavior as part of disabling the preview UI.

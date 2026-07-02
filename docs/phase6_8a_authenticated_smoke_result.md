# Phase 6.8A Authenticated Smoke Result

Date: 2026-07-02

## Scope

This document records the Phase 6.8A authenticated smoke result.

This result does not mean:

- Deployment was performed.
- Durable Memory write was enabled.
- Real Firestore Memory runtime was connected.
- Mock auth was enabled.
- A fake token was used.

## Token Handling

- `FIREBASE_ID_TOKEN`: present, temporary test account token used.
- Full token was not recorded.
- Fake token: no.
- Mock auth: no.
- Mock LLM: no.

## Commands

Agent life_event preview-only smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-agent-life-event-write.mjs
```

Write flags intentionally not set:

- `RUN_AGENT_WRITE_SMOKE=true`: not set.
- `EXPECT_AGENT_WRITE_ENABLED=true`: not set.

RAG / Agent Preview smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-rag-e2e.mjs
```

Mutating flag intentionally not set:

- `RUN_MUTATING_SMOKE=true`: not set.

## Result

Agent life_event smoke:

```text
PASS API /health returns healthy
PASS Agent proposes life_event action
PASS Confirm action and verify expected write mode
PASS Repeat confirm and verify idempotency
SKIP Real write assertions: Set RUN_AGENT_WRITE_SMOKE=true and EXPECT_AGENT_WRITE_ENABLED=true to require wroteData=true.
```

RAG / Agent Preview smoke:

```text
PASS API /health returns healthy
PASS API endpoint responds
PASS Web endpoint is reachable
PASS Agent Preview lists documents
PASS Agent Preview proposes reminder confirmation
SKIP Authenticated upload/RAG/delete flow: RUN_MUTATING_SMOKE=true is required to create and delete a temporary test document.
```

## No-write Verification

| Check | Result |
| --- | --- |
| Deployment performed | no |
| Push performed | no |
| Cloud Run env modified | no |
| Firestore Rules modified | no |
| MCP modified | no |
| Durable Memory write enabled | no |
| Real Firestore Memory runtime connected | no |
| Writes `users/{userId}/memories` | no |
| Writes `life_events` | no |
| Mock auth enabled | no |
| Mock LLM enabled | no |
| `RUN_AGENT_WRITE_SMOKE` enabled | no |
| `EXPECT_AGENT_WRITE_ENABLED` enabled | no |
| `RUN_MUTATING_SMOKE` enabled | no |

## Final Conclusion

Phase 6.8A authenticated smoke completed.

Current status:

- API health: PASS.
- `smoke-agent-life-event-write`: PASS.
- `smoke-rag-e2e`: PASS.
- Real write assertions were intentionally skipped because write flags remained
  disabled.
- Mutating RAG upload/delete flow was intentionally skipped because
  `RUN_MUTATING_SMOKE` remained disabled.
- No durable Memory write was enabled.
- No real Firestore Memory runtime was connected.
- No deployment or push was performed.

Next step:

- Submit this result document for review.
- Do not deploy or enable durable Memory write without separate user approval.

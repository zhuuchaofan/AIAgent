# Phase 5.7 Authenticated Preview-Only Smoke Result

## Scope

Phase 5.7 verified the deployed preview-only API with a real authenticated Firebase ID token.

This phase did not enable real writes, did not modify Cloud Run env, did not deploy, did not modify Firestore Rules, and did not modify Firebase Auth or Web.

## Deployment Baseline

Phase 5.7B planner routing fix:

```text
f64b5cb
```

API revision under test:

```text
life-agent-api-00035-tnf
```

Web revision:

```text
life-agent-web-00018-bpq
```

Cloud Run traffic:

```text
life-agent-api-00035-tnf: 100%
```

Cloud Run URL:

```text
https://life-agent-api-hyo2yvwwia-uc.a.run.app
```

## Cloud Run Env

Production env remained preview-only:

```text
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set
ENABLE_CREATE_LIFE_EVENT_TOOL: not set
```

Because both Agent write flags are unset, real `create_life_event` writes remain disabled.

## Initial Phase 5.7 Blocker

The first authenticated smoke attempt did not fail because of token, deployment, or Cloud Run env.

The blocker was planner routing:

- Explicit life-event text was not recognized by the deployed `AgentRunner`.
- `/api/agent/run` returned `mode=preview_readonly_rag`.
- `requiresConfirmation=false`.
- `proposedAction=null`.

Phase 5.7B fixed this by routing explicit `life_event`, `生活事件记录`, and `create_life_event` inputs to a `create_life_event` proposed action with a payload compatible with `LifeEventActionPayloadMapper`.

## smoke-agent-life-event-write Result

Command executed by the user in the authenticated shell:

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

Validated behavior:

- Authenticated Agent flow passed.
- LifeEvent proposed action was generated.
- Confirm succeeded.
- Repeat confirm succeeded idempotently.
- Real write assertions were skipped because real-write smoke env vars were not set.
- `RUN_AGENT_WRITE_SMOKE=true` was not set.
- `EXPECT_AGENT_WRITE_ENABLED=true` was not set.

Expected preview-only semantics held:

- `previewOnly=true`.
- `wroteData=false`.
- No real `createdResourceId` was required.
- No `life_event` was created.

## smoke-rag-e2e Result

Command executed by the user in the authenticated shell:

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

Validated behavior:

- API health passed.
- API endpoint responded.
- Web endpoint remained reachable.
- Existing Agent Preview list-documents flow passed.
- Existing reminder confirmation preview flow passed.
- Mutating RAG upload/query/delete flow was not executed because `RUN_MUTATING_SMOKE=true` was not set.

## Safety Result

Phase 5.7 authenticated preview-only smoke did not enable or perform real writes.

Safety checks:

- No `wroteData=true` result was observed.
- No `life_event` was created.
- Cloud Run write flags remained unset.
- Cloud Run mock auth remained disabled.
- Cloud Run mock LLM remained disabled.
- Web revision did not change.
- Firestore Rules were not changed.
- Firebase Auth was not changed.

## Conclusion

Phase 5.7 authenticated preview-only production smoke passed.

Current recommendation:

- Phase 5 Development can be closed.
- Do not enable real writes as part of development.
- Real-write canary, production enablement, and gradual rollout belong to the Release Gate and require separate explicit approval.

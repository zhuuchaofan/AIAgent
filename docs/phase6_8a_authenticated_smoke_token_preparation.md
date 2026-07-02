# Phase 6.8A Authenticated Smoke Token Preparation

Date: 2026-07-02

## Scope

This document prepares for Phase 6.8A authenticated smoke by recording the
current `FIREBASE_ID_TOKEN` blocker and the safe token handling procedure.

This task does not:

- Deploy.
- Execute smoke.
- Modify code.
- Modify Cloud Run env.
- Enable durable Memory write.
- Enable mock auth.
- Use a fake token.
- Modify Firestore Rules.
- Modify MCP.

## Current Readiness State

Current Cloud Run readiness:

- Cloud Run env describe has succeeded.
- Current API revision: `life-agent-api-00038-w9d`
- `ENABLE_AGENT_WRITE_TOOLS`: closed / unset.
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: closed / unset.
- Durable Memory write flag: absent / not enabled.
- `USE_MOCK_AUTH=false`.
- `USE_MOCK_LLM=false`.

Current blocker:

- `FIREBASE_ID_TOKEN` is missing.
- Current readiness: `PARTIAL READY - FIREBASE_ID_TOKEN MISSING`.

## Token Handling Rules

Rules for any future authenticated smoke:

- Do not write the full `FIREBASE_ID_TOKEN` into docs.
- Do not print the full token in terminal output.
- Only check the first 40 characters when confirming presence.
- Do not use fake tokens.
- Do not enable mock auth.
- After token use, the user should consider signing out, refreshing the login
  state, or otherwise invalidating the session if appropriate.

## How To Obtain `FIREBASE_ID_TOKEN`

The user should obtain a valid Firebase ID token from the production Web app:

1. Open `https://life.zhuchaofan.com`.
2. Sign in normally.
3. Use browser DevTools / Console / cookies / authenticated requests to obtain a
   valid Firebase ID token.
4. Set the token in the current shell:

```bash
export FIREBASE_ID_TOKEN='...'
```

Do not write the real token into this document.

## Pre-smoke Check Commands

After the token is provided, run:

```bash
echo "$FIREBASE_ID_TOKEN" | cut -c1-40
git status --branch --short
git status --short
```

If the token check prints nothing:

- Stop.
- Do not deploy.
- Do not enable mock auth.
- Record the smoke as still blocked.

## Authenticated Smoke Commands

These commands should be run only after the user provides a valid
`FIREBASE_ID_TOKEN` and separately approves authenticated smoke.

Agent life_event preview-only smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-agent-life-event-write.mjs
```

RAG / Agent Preview smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
node scripts/smoke-rag-e2e.mjs
```

Phase 6.8A dedicated Memory proposal smoke script:

- Not available / pending.
- Existing scripts found:
  - `scripts/smoke-agent-life-event-write.mjs`
  - `scripts/smoke-rag-e2e.mjs`
- Do not invent a new script name for this preparation step.

## Expected Results

Expected authenticated smoke behavior:

- `/health`: PASS.
- life_event preview-only smoke: PASS.
- RAG smoke: PASS.
- `save_memory_preview` / Memory proposal behavior remains preview-only.
- `wroteData=false`.
- `previewOnly=true` where applicable.
- No `users/{userId}/memories` document is created.
- No `life_events` document is created by Memory proposal runtime.
- Durable Memory write remains disabled.

## Stop Conditions

Stop immediately if any of these occur:

- Token is empty.
- Token is invalid or requests return 401.
- `wroteData=true`.
- `users/{userId}/memories` is created.
- `life_events` is created by Memory proposal runtime.
- Cloud Run write flags are enabled.
- Mock auth is enabled.
- Durable Memory write is enabled.
- Smoke regression appears.

## Next Step

Current task does not execute smoke.

Next step:

- The user must provide `FIREBASE_ID_TOKEN`.
- After token is provided, authenticated smoke can proceed only with user
  approval.
- Deployment still requires separate approval.

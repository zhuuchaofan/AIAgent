# Personal Agent v2 Firestore Persistence Enablement Result

Date: 2026-07-11

## Executive Summary

Personal Agent v2 Firestore pending action persistence was enabled for the API
Cloud Run service and deployed as a preview-only rollout.

Current conclusion: **deployed with authenticated smoke pending**.

The rollout enabled only the Personal Agent v2 pending action state-memory
store. It did not enable memories, life events, real tool execution, external
provider execution, Firestore Rules changes, Firebase config changes, IAM
changes, or web direct Firestore access.

## Approval

The user approved:

- Firestore persistence env enablement for Personal Agent v2
- Cloud Run deployment
- authenticated smoke

Authenticated smoke could not be completed because no usable Firebase ID token
was available.

## Scope

Enabled scope:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Out of scope and not enabled:

- `users/{userId}/memories`
- `life_events`
- real tool execution
- external provider execution
- legacy `/api/agent/confirm`
- frontend direct Firestore access

## Project / Region

- project: `copper-affinity-467409-k7`
- region: `us-central1`
- API service: `life-agent-api`
- Web service: `life-agent-web`
- Web domain: `https://life.zhuchaofan.com/`

## Pre-deployment State

API:

- previous latest ready revision: `life-agent-api-00041-w2n`
- previous traffic: `life-agent-api-00041-w2n` at 100%
- URL: `https://life-agent-api-hyo2yvwwia-uc.a.run.app`

Web:

- latest ready revision: `life-agent-web-00020-rp7`
- traffic: `life-agent-web-00020-rp7` at 100%
- URL: `https://life-agent-web-hyo2yvwwia-uc.a.run.app`

## Local Gate Results

Passed:

- `git diff --check`
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
  - result: 404 passed
- `npm --prefix life-agent-web run lint`
- `npm --prefix life-agent-web run build`

Build note:

- The first web build failed because the sandbox could not fetch Google Fonts
  for `next/font`.
- The same command passed after network access was allowed.

## Env Changes

Changed only API pending action persistence env:

```text
AGENT_PENDING_ACTION_STORE_MODE=firestore
AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true
AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true
```

Dangerous write flags:

- `ENABLE_AGENT_WRITE_TOOLS`: not set
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set
- dangerous write combo: false

Mock flags:

- `USE_MOCK_AUTH=false`
- `USE_MOCK_LLM=false`

No secret values were changed or printed in this result.

## Deployment Result

API deployment command shape:

```text
gcloud run deploy life-agent-api --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated \
  --update-env-vars AGENT_PENDING_ACTION_STORE_MODE=firestore,AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true,AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true
```

API:

- new latest ready revision: `life-agent-api-00042-zhr`
- traffic: `life-agent-api-00042-zhr` at 100%
- service ready: yes

Web:

- not redeployed
- reason: no frontend code or web build-env change was required; existing web
  service already points to the API URL and Agent Preview remains enabled.
- latest ready revision remains `life-agent-web-00020-rp7`
- traffic remains `life-agent-web-00020-rp7` at 100%

## Smoke Results

Unauthenticated / service smoke:

- API `/health`: HTTP 200, body `"healthy"`
- unauthenticated `GET /api/agent/pending-actions`: HTTP 401
- `https://life.zhuchaofan.com/`: HTTP 200
- smoke script health check: PASS after network access was allowed

Authenticated smoke:

- status: pending
- reason: `FIREBASE_ID_TOKEN` was not present in the shell
- `FIREBASE_ID_TOKEN_B`: not present
- authenticated create/list/confirm/cancel/refresh checks were not run
- browser login attempt: the in-app browser Google login flow did not provide a
  stable logged-in session for token extraction
- Firebase Auth email/password token attempt: returned
  `INVALID_LOGIN_CREDENTIALS` for the supplied test account credentials

Script output summary:

```text
PASS API /health returns healthy
SKIP Authenticated Personal Agent v2 flow: FIREBASE_ID_TOKEN is not set.
```

## Safety Verification

Confirmed after deployment:

- API latest ready revision is serving 100% traffic.
- API pending action persistence env is set to Firestore preview-only mode.
- `USE_MOCK_AUTH` is false.
- `USE_MOCK_LLM` is false.
- dangerous agent write flags are not set.
- no Web service env change was made.
- no `firestore.rules` change was made.
- no `firebase.json` change was made.
- no IAM change was made.
- no memory or life-event env flag was enabled.

Not directly verified yet:

- authenticated Firestore-backed create/read/confirm/cancel persistence
- browser refresh restore with authenticated user
- cross-user deployed owner-isolation smoke

These require a real Firebase ID token or authenticated browser session.

## Data / Execution Impact

- Real Firestore pending action writes: enabled only for preview pending action
  state under `users/{userId}/pendingActions/{pendingActionId}`.
- Real `memories` writes: no
- Real `life_events` writes: no
- Real tool execution: no
- External provider execution: no
- Legacy `/api/agent/confirm` usage for Personal Agent v2: no

## Rollback

Rollback was not executed because service readiness and unauthenticated smoke
passed.

Rollback options remain:

- set `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=false`
- or set `AGENT_PENDING_ACTION_STORE_MODE=in_memory`
- or set `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=false`
- or shift traffic back to `life-agent-api-00041-w2n`

Any rollback must keep dangerous write flags unset or false.

## Final Conclusion

Personal Agent v2 Firestore persistence preview has been deployed at the API
layer, but Personal Agent v2 is not fully complete until authenticated smoke
proves:

- create persists in Firestore
- refresh restores the pending action
- confirm persists
- cancel persists
- historical actions remain visible
- owner isolation works in deployed environment
- `executed=false`
- `wroteData=false`
- no legacy confirm path
- no `life_events`
- no `memories`
- no real tool execution

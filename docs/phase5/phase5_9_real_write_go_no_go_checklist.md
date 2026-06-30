# Phase 5.9 Real Write Go/No-Go Checklist

## Scope

Phase 5.9 is a Release Gate decision checklist before enabling real Agent `create_life_event` writes.

This phase does not deploy, does not modify Cloud Run env, does not enable write flags, does not execute real writes, and does not modify Firestore Rules or Firebase Auth.

Phase 5 Development is closed. Real write enablement is only allowed in a later explicitly approved Release Gate canary execution step.

## Current Baseline

Completed baseline:

- Phase 5.7 authenticated preview-only production smoke passed.
- Phase 5.7B planner routing fix was committed: `f64b5cb`.
- Phase 5.7C result documentation was committed: `19b478b`.
- Phase 5.8 real-write enablement plan was completed and committed: `eaa089c`.
- API revision under preview-only validation: `life-agent-api-00035-tnf`.
- Web revision: `life-agent-web-00018-bpq`.
- Production Agent write flags remain unset.
- No real `life_event` write was executed.
- No `life_event` was created by Agent smoke.

Current production safety baseline:

```text
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
ENABLE_AGENT_WRITE_TOOLS: not set
ENABLE_CREATE_LIFE_EVENT_TOOL: not set
```

## GitHub Push Blocker

Local `main` is ahead of `origin/main`, but push is currently blocked.

Observed remote:

```text
origin https://github.com/zhuuchaofan/AIAgent.git
```

Observed push failure:

```text
fatal: could not read Username for 'https://github.com': Device not configured
```

Go condition:

- GitHub credentials are fixed locally, or the remote is intentionally switched to SSH.
- Local commits are pushed to `origin/main`.
- No local-only production enablement work is used as the source of truth for a risky operation.

No-go condition:

- `main` remains ahead of `origin/main` because push is blocked.
- Credential workarounds require storing tokens in repository files or committed config.

Do not write GitHub tokens into code, docs, env files, or credential files from this workflow.

## Go/No-Go Summary

Do not proceed to real-write enablement unless every section below is `GO`.

| Area | Required State |
| --- | --- |
| Git history | Phase 5 commits pushed or explicitly accepted as local-only by the user |
| Firestore Rules | Rules decision complete and risk accepted |
| Cross-project Auth | Behavior verified or not relevant to backend Admin SDK write |
| Smoke user | Dedicated test user available |
| Cloud Run flags | Current state confirmed false/unset before enablement |
| Rollback | Commands and previous revision identified |
| Cleanup | Smoke data cleanup process ready |
| Observability | Logs can identify actionId/userId/result/error |
| Real-write smoke | Command and expected assertions ready |
| Approval | User explicitly approves Phase 5.10 canary |

## Firestore Rules Checklist

Go conditions:

- `users/{userId}/life_events/{eventId}` rule posture is decided.
- If client SDK reads are needed, owner read behavior is verified.
- Client direct create/update/delete is either prohibited or explicitly accepted with rationale.
- Backend Admin SDK write path is understood to bypass Firestore Rules.
- No Firestore Rules deployment is bundled with real-write flag enablement unless separately approved.

No-go conditions:

- Rules still allow unwanted client direct writes without accepted rationale.
- Rules deployment status is unknown.
- Rules changes are attempted in the same step as write flag enablement without a rollback plan.

## Cross-Project Auth Checklist

Known projects:

```text
Firestore project: copper-affinity-467409-k7
Firebase Auth project: my-agent-app-a5e42
```

Go conditions:

- Backend API verifies Firebase ID tokens with `FIREBASE_PROJECT_ID=my-agent-app-a5e42`.
- Backend Admin SDK writes to Firestore project `copper-affinity-467409-k7`.
- The canary does not depend on client SDK direct Firestore writes.
- If client SDK reads are used, `request.auth.uid == userId` behavior has been verified against the Firestore project.

No-go conditions:

- The canary assumes Firestore Rules can verify cross-project auth without proof.
- Frontend direct Firestore read/write behavior is required but unverified.

## Dedicated Smoke Test User Checklist

Go conditions:

- A dedicated test user is available.
- The user id is known for Firestore verification.
- The test account contains no real personal data needed for production use.
- Smoke input uses `[SMOKE TEST]`.
- The test user is the only account used for real-write smoke.

No-go conditions:

- The test would run against a real personal user account.
- The test user id cannot be determined.
- Cleanup cannot confidently distinguish smoke data from real data.

## Cloud Run Flags Checklist

Before enablement, confirm:

```bash
gcloud run services describe life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --format="yaml(status.url,status.traffic,spec.template.spec.containers[0].env)"
```

Go conditions before enabling:

- `USE_MOCK_AUTH=false`.
- `USE_MOCK_LLM=false`.
- `ENABLE_AGENT_WRITE_TOOLS` is unset or false.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is unset or false.
- Current revision and previous rollback revision are known.

Canary enablement requires both flags:

```text
ENABLE_AGENT_WRITE_TOOLS=true
ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

No-go conditions:

- Either write flag is already true before the planned canary.
- Mock auth or mock LLM is enabled in production.
- Current traffic points to an unexpected revision.

## Rollback Readiness Checklist

Go conditions:

- Flag rollback command is ready:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --remove-env-vars ENABLE_AGENT_WRITE_TOOLS,ENABLE_CREATE_LIFE_EVENT_TOOL
```

- Explicit false rollback command is ready:

```bash
gcloud run services update life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --set-env-vars ENABLE_AGENT_WRITE_TOOLS=false,ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

- Previous known-good revision is recorded.
- Traffic rollback command is ready:

```bash
gcloud run services update-traffic life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --to-revisions <previous-revision>=100
```

No-go conditions:

- No previous healthy revision is known.
- The operator cannot verify new revision traffic.
- Rollback would require code changes or manual credential repair during an incident.

## Cleanup Procedure Checklist

Go conditions:

- Smoke-created data includes `[SMOKE TEST]`.
- `createdResourceId=evt_{agentActionId}` is recorded.
- Cleanup is limited to `users/{testUserId}/life_events`.
- Only documents with clear smoke markers are eligible for deletion.
- Matching pending actions under `users/{testUserId}/agent_pending_actions` can be inspected.
- Cleanup does not touch non-smoke user data.

No-go conditions:

- Smoke markers are missing.
- Cleanup requires bulk delete across multiple users.
- The canary cannot identify the created event id.

## Observability And Logging Checklist

Go conditions:

- Cloud Run logs can be queried for:
  - `actionId`
  - `userId`
  - `actionType`
  - feature gate result
  - `previewOnly`
  - `wroteData`
  - `createdResourceType`
  - `createdResourceId`
  - `write_failed`
  - `invalid_payload`
  - duplicate confirm / idempotent return
  - cross-user reject
- Logs do not include full payload JSON, auth headers, Firebase tokens, secrets, or private content.

No-go conditions:

- Write success/failure cannot be traced in logs.
- Sensitive values appear in logs.
- Idempotent duplicate confirm cannot be distinguished from a second write.

## Authenticated Real-Write Smoke Readiness

Command to run only in Phase 5.10 after explicit approval:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$FIREBASE_ID_TOKEN" \
RUN_AGENT_WRITE_SMOKE=true \
EXPECT_AGENT_WRITE_ENABLED=true \
SMOKE_TEST_PREFIX="[SMOKE TEST]" \
node scripts/smoke-agent-life-event-write.mjs
```

Expected assertions:

- `/health` passes.
- Agent proposes `create_life_event`.
- Confirm returns `previewOnly=false`.
- Confirm returns `wroteData=true`.
- Confirm returns `createdResourceType=life_event`.
- Confirm returns `createdResourceId=evt_{agentActionId}`.
- Repeated confirm returns the same `createdResourceId`.
- Firestore has exactly one corresponding `life_event`.

No-go conditions:

- `FIREBASE_ID_TOKEN` is missing.
- `RUN_AGENT_WRITE_SMOKE=true` or `EXPECT_AGENT_WRITE_ENABLED=true` is set accidentally before the approved canary.
- The smoke user is not dedicated.

## Phase 5.9 Decision

Phase 5.9 does not allow enabling real writes.

Real write enablement must not happen in this phase.

The earliest allowed real-write step is a separately approved Release Gate action:

```text
Controlled Real-Write Canary Execution
```

It must be explicitly approved again before any of the following occur:

- Set `ENABLE_AGENT_WRITE_TOOLS=true`.
- Set `ENABLE_CREATE_LIFE_EVENT_TOOL=true`.
- Set `RUN_AGENT_WRITE_SMOKE=true`.
- Set `EXPECT_AGENT_WRITE_ENABLED=true`.
- Execute a real `create_life_event` write.

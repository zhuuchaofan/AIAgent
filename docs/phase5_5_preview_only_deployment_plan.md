# Phase 5.5 Preview-Only Deployment Plan

## Scope

Phase 5.5 prepares a deployment plan for an API revision that contains the Agent `create_life_event` real-write code path while keeping production write flags disabled.

This phase does not deploy, does not change Cloud Run env, does not enable real writes, does not modify Firestore Rules, and does not modify Firebase Auth, frontend code, or backend code.

## Deployment Goal

Deploy an API revision that includes Phase 4.8 and Phase 5.x Agent write-path code, but keep production behavior preview-only.

Required production flag state:

```text
ENABLE_AGENT_WRITE_TOOLS=false or unset
ENABLE_CREATE_LIFE_EVENT_TOOL=false or unset
```

Expected production behavior after deployment:

- `/api/agent/confirm` can recognize `create_life_event`.
- Feature gate remains closed.
- `previewOnly=true`.
- `wroteData=false`.
- No `users/{userId}/life_events/{eventId}` document is created.

## Pre-Deployment Checks

Run from a clean local workspace:

```bash
git status --branch --short
git status --short
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
npm run lint --prefix life-agent-web
npm run build --prefix life-agent-web
git diff --check
```

If only the API is being deployed, the web lint/build is not strictly required for the API artifact, but the full gate is recommended before any production-facing deployment because Agent Preview and RAG workflows cross the API/Web boundary.

Before deployment, confirm all intended Phase 4.8/5.x commits are local and pushed or otherwise reproducible from the deployment source.

## Cloud Run Current Env Check

Use a read-only describe command before deployment:

```bash
gcloud run services describe life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --format="yaml(status.url,status.traffic,spec.template.spec.containers[0].env)"
```

Required env state:

- `ENABLE_AGENT_WRITE_TOOLS` is unset or `false`.
- `ENABLE_CREATE_LIFE_EVENT_TOOL` is unset or `false`.
- `USE_MOCK_AUTH=false`.
- `USE_MOCK_LLM=false`.

If either write flag is `true`, stop before deployment and resolve the env state.

## Deployment Command Draft

Use the project's existing API deployment workflow or deployment script. Do not create a new deployment system for Phase 5.5.

Current repository deployment references include:

- `./deploy.sh`
- `docs/cloud-run-deploy-skill.md`

Deployment constraints:

- Do not pass `ENABLE_AGENT_WRITE_TOOLS=true`.
- Do not pass `ENABLE_CREATE_LIFE_EVENT_TOOL=true`.
- Do not modify Firestore Rules.
- Do not modify Firebase Auth.
- Do not deploy Web unless a separate Web change requires it.

If using `gcloud run deploy` directly, preserve existing required production env such as auth, Firestore, Firebase, and LLM settings, and explicitly avoid enabling Agent write flags.

## Post-Deployment Verification

Health check:

```bash
curl https://life-agent-api-151587524132.us-central1.run.app/health
```

Expected response:

```text
healthy
```

Preview-only Agent write smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
node scripts/smoke-agent-life-event-write.mjs
```

Without `FIREBASE_ID_TOKEN`, authenticated Agent flow should skip safely.

With `FIREBASE_ID_TOKEN` and write flags still disabled:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="..." \
node scripts/smoke-agent-life-event-write.mjs
```

Expected authenticated result:

- Agent run returns `requiresConfirmation=true`.
- Proposed action is `create_life_event` or `create_life_event_preview`.
- Confirm returns `previewOnly=true`.
- Confirm returns `wroteData=false`.
- `createdResourceId` is empty.
- No `life_event` is created.

Do not set `RUN_AGENT_WRITE_SMOKE=true` for preview-only deployment verification.

## Existing Feature Regression Checks

Verify existing functionality after deployment:

- RAG Q&A still works.
- `list_documents` still works.
- Agent Preview is still visible in Web.
- Agent proposed actions are still generated.
- Confirm preview-only flow still succeeds.
- Unauthenticated API calls that require auth still return `401`.
- Existing RAG upload/query/delete smoke remains gated behind its own safe env flags.

Useful smoke command:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
node scripts/smoke-rag-e2e.mjs
```

With no `FIREBASE_ID_TOKEN`, authenticated flows should skip rather than fail.

## Rollback Plan

If the new revision has a bug, route traffic back to the previous known-good revision:

```bash
gcloud run services update-traffic life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --to-revisions <previous-revision>=100
```

Rollback notes:

- Revision rollback does not delete data.
- A preview-only deployment should not create `life_event` documents.
- If write flags were accidentally enabled, first disable the flags.
- If the revision itself is faulty, roll back traffic after disabling write flags as needed.
- Re-run `/health` and preview-only smoke after rollback.

## Stop Conditions

Stop deployment or rollback immediately if any of the following occurs:

- `ENABLE_AGENT_WRITE_TOOLS=true`.
- `ENABLE_CREATE_LIFE_EVENT_TOOL=true`.
- `confirm` returns `wroteData=true` during preview-only verification.
- Any unexpected `life_event` document is created.
- RAG main path fails.
- Agent Preview cannot load.
- Firestore permission errors appear.
- Authenticated requests fail unexpectedly.
- Unauthenticated protected calls no longer return `401`.
- Cloud Run traffic is split to an unexpected revision.

## Phase 5.5 Conclusion

Phase 5.5 does not deploy and does not enable real writes.

This document is only the preview-only API deployment plan. Executing the preview-only deployment requires separate user confirmation.

The next operational step, if approved separately, is to deploy the API code with both Agent write flags still unset or false, then run the post-deployment preview-only checks above.

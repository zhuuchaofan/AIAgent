# LifeOS Personal Home v1.1 Deployment Result

Date: 2026-07-11
Deployment time: 2026-07-11T09:10:25Z

## Summary

LifeOS Personal Home v1.1 was deployed to Cloud Run preview. This release
connects the free-text Personal Home input to the current pending action
mainline and adds non-destructive pending action history cleanup.

Current conclusion: **deployed successfully with authenticated smoke pending**.

## Services

Project: `copper-affinity-467409-k7`
Region: `us-central1`

| Service | Before revision | After revision | Traffic |
| --- | --- | --- | --- |
| `life-agent-api` | `life-agent-api-00042-zhr` | `life-agent-api-00043-vnx` | 100% to new revision |
| `life-agent-web` | `life-agent-web-00021-7bx` | `life-agent-web-00022-zmz` | 100% to new revision |

API URL: `https://life-agent-api-151587524132.us-central1.run.app`
Web Cloud Run URL: `https://life-agent-web-151587524132.us-central1.run.app`
Public URL: `https://life.zhuchaofan.com/`

## What Changed

- The Personal Home free-text input now creates pending actions through
  `/api/agent/pending-actions`.
- The free-text input no longer calls legacy `/api/agent/run`.
- Personal Home confirm/cancel still uses the current pending action route and
  does not call legacy `/api/agent/confirm`.
- Added owner-scoped archive/hide support:
  - `POST /api/agent/pending-actions/{actionId}/archive`
  - sets `isArchived`, `archivedAt`, and `archivedByUserId`
  - does not hard-delete Firestore documents
- The UI defaults to a shorter history view and can show more history on demand.
- Terminal history records can be hidden from the default list.

## Environment / Safety

Cloud Run env was not modified by this deployment.

API flags after deployment:

- `ENABLE_AGENT_WRITE_TOOLS`: not set
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set
- dangerous write combo: false
- `USE_MOCK_AUTH`: `false`
- `USE_MOCK_LLM`: `false`
- `AGENT_PENDING_ACTION_STORE_MODE`: `firestore`
- `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE`: `true`
- `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY`: `true`

Web flags after deployment:

- `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW`: `true`

This deployment did not:

- modify Cloud Run env
- modify `firestore.rules`
- modify `firebase.json`
- modify package files or lockfiles
- write `memories`
- write `life_events`
- execute real tool actions
- call external provider APIs
- hard-delete Firestore pending action documents

## Smoke Results

Local validation before deployment:

- `git status --short`: clean before implementation; clean after commit is
  checked separately
- `git diff --check`: passed
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`: 406 passed
- `npm --prefix life-agent-web run lint`: passed
- `npm --prefix life-agent-web run build`: passed after rerunning with network
  access for Google Fonts

Deployment smoke:

- API `/health`: HTTP 200
- Web Cloud Run URL `/`: HTTP 200
- `https://life.zhuchaofan.com/`: HTTP 200
- unauthenticated `/api/agent/pending-actions`: HTTP 401
- post-deploy Cloud Run describe: API and Web both 100% traffic to new revisions
- post-deploy env check: no dangerous write flags enabled

Authenticated smoke:

- Pending. A user screenshot after deployment showed the expected Personal Home
  v1.1 history controls, including `显示更多历史`, confirming the web revision is
  visible online.
- Manual follow-up should verify that entering
  `提醒我七月十五号去取身份证` creates a new pending action card through the
  current `/api/agent/pending-actions` path.

## Rollback

No rollback was performed.

If rollback is needed:

- route `life-agent-api` traffic back to `life-agent-api-00042-zhr`
- route `life-agent-web` traffic back to `life-agent-web-00021-7bx`
- keep env unchanged

No data rollback is expected because the archive feature is non-destructive and
pending action writes remain preview-only under:

```text
users/{userId}/pendingActions/{pendingActionId}
```

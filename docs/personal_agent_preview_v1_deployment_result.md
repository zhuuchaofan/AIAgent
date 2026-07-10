# Personal Agent Preview v1 Deployment Result

## Deployment time

- UTC: 2026-07-10T01:35:18Z
- Local: 2026-07-10 09:35:18 CST

## Scope

- Project: `copper-affinity-467409-k7`
- Region: `us-central1`
- API service: `life-agent-api`
- Web service: `life-agent-web`
- Deployment mode: Cloud Run preview-only

## Deployment commands

The deployment intentionally did not use `--set-env-vars`,
`--update-env-vars`, or `--remove-env-vars`.

```bash
gcloud run deploy life-agent-api \
  --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated \
  --quiet

gcloud run deploy life-agent-web \
  --source ./life-agent-web \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated \
  --quiet
```

The existing `life-agent-web/deploy.sh` was not used because it passes env
mutation flags, which were explicitly out of scope for this preview-only
deployment.

## Revisions

| Service | Before revision | After revision | Traffic |
|---|---|---|---|
| `life-agent-api` | `life-agent-api-00040-z5b` | `life-agent-api-00041-w2n` | 100% to `life-agent-api-00041-w2n` |
| `life-agent-web` | `life-agent-web-00019-876` | `life-agent-web-00020-rp7` | 100% to `life-agent-web-00020-rp7` |

## Environment safety

Cloud Run env was read-only checked before and after deployment.

### API flags

| Flag | Status |
|---|---|
| `ENABLE_AGENT_WRITE_TOOLS` | not set |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | not set |
| Dangerous write combo | false |
| `USE_MOCK_AUTH` | `false` |
| `USE_MOCK_LLM` | `false` |

### Web flags

| Flag | Status |
|---|---|
| `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` | `true` |

No Cloud Run env mutation command was used.

## Data and execution safety

- Connected to real Firestore: no
- Created `pendingActions` collection: no
- Wrote `users/{userId}/pendingActions`: no
- Wrote `users/{userId}/memories`: no
- Wrote `life_events`: no
- Executed real tool action: no
- Called external provider API as part of this deployment: no
- Modified `firestore.rules`: no
- Modified `firebase.json`: no
- Modified package files: no

## Pre-deployment verification

| Check | Result |
|---|---|
| `git status --short` | clean |
| `git diff --check` | passed |
| `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj` | passed, 376 tests |
| `npm --prefix life-agent-web run lint` | passed |
| `npm run build --prefix life-agent-web` | passed |

## Smoke results

| Smoke | Result |
|---|---|
| API `/health` | HTTP 200, body `"healthy"` |
| Web Cloud Run URL | HTTP 200 |
| Custom domain `https://life.zhuchaofan.com/` | HTTP 200 |
| Unauthenticated demo API | HTTP 401 |
| Web bundle contains Personal Agent Preview v1 copy | confirmed |
| Web bundle contains in-memory warning copy | confirmed |
| Web bundle contains `confirmed != executed` copy | confirmed |
| Web bundle contains `executed`, `wroteData`, `legacyConfirm`, `realWritePath` fields | confirmed |
| Confirm/cancel terminal-state UI | deployed in bundle; authenticated click smoke pending |

## Authenticated smoke

Authenticated browser smoke was not completed because no Firebase ID token was
available in this execution environment. Results were not fabricated.

Pending authenticated checks:

- login and open Agent Preview;
- generate pending action;
- confirm pending action;
- cancel pending action;
- verify confirmed / cancelled cards no longer show actionable confirm/cancel;
- verify UI shows `executed=false`;
- verify UI shows `wroteData=false`;
- verify UI shows `legacyConfirm=false`;
- verify UI shows `realWritePath=false`;
- verify in-memory preview warning is visible.

## Rollback

- Rollback performed: no
- Reason: both services deployed successfully, became ready, and routed 100%
  traffic to the new revisions.
- If rollback is needed later, route traffic back to:
  - API: `life-agent-api-00040-z5b`
  - Web: `life-agent-web-00019-876`

## Conclusion

Personal Agent Preview v1 was deployed to Cloud Run in preview-only mode.

Current conclusion: deployed successfully with authenticated smoke pending.

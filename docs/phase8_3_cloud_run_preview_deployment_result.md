# Phase 8.3 Cloud Run Preview Deployment Result

Date: 2026-07-09

## 1. Deployment Summary

Result: **deployed with authenticated smoke pending**.

Phase 8.3 deployed the current local `main` code to Cloud Run for preview-only
validation. Both API and Web services deployed successfully and now route 100%
traffic to the new revisions.

No push was performed.

## 2. Project / Region

- project: `copper-affinity-467409-k7`
- region: `us-central1`
- API service: `life-agent-api`
- Web service: `life-agent-web`

## 3. Deployment Scope

Deployed:

- `life-agent-api`
- `life-agent-web`

Reason:

- Phase 8.1 modified API/runtime response fields and frontend Agent Preview UI.

Deployment commands intentionally did not use:

- `--set-env-vars`
- `--update-env-vars`
- `--remove-env-vars`
- `gcloud run services update`
- `gcloud run services replace`

The existing `life-agent-web/deploy.sh` was not used because it passes
`--set-env-vars`, which was out of scope for Phase 8.3.

## 4. Revision / Traffic

### API

Before deployment:

- latest ready revision: `life-agent-api-00039-w2d`
- traffic: 100% to `life-agent-api-00039-w2d`

After deployment:

- latest ready revision: `life-agent-api-00040-z5b`
- traffic: 100% to `life-agent-api-00040-z5b`

### Web

Before deployment:

- latest ready revision: `life-agent-web-00018-bpq`
- traffic: 100% to `life-agent-web-00018-bpq`

After deployment:

- latest ready revision: `life-agent-web-00019-876`
- traffic: 100% to `life-agent-web-00019-876`

## 5. Cloud Run Env / Flag Status

Cloud Run env was checked read-only before and after deployment.

API after deployment:

| Flag | Present | True | Status |
| --- | --- | --- | --- |
| `ENABLE_AGENT_WRITE_TOOLS` | no | no | safe |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | no | no | safe |
| `USE_MOCK_AUTH` | yes | no | safe |
| `USE_MOCK_LLM` | yes | no | safe |

Dangerous Agent write flag combination:

```text
ENABLE_AGENT_WRITE_TOOLS=true
ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

Result: not found.

Web after deployment:

| Flag | Present | True | Status |
| --- | --- | --- | --- |
| `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` | yes | yes | expected for preview visibility |
| `API_BASE_URL` | yes | no | present; value not recorded |

Secret values and Firebase config values were not recorded.

## 6. Safety Assertions

This deployment did not:

- modify Cloud Run env
- enable Agent write flags
- modify production Firebase config
- modify `firestore.rules`
- modify `firebase.json`
- install dependencies
- connect real Firestore as part of smoke
- create Firestore collections
- write `users/{userId}/pendingActions`
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- call external provider APIs

Phase 8 demo code path remains:

```text
/api/agent/pending-actions/demo
/api/agent/pending-actions/demo/{actionId}/confirm
/api/agent/pending-actions/demo/{actionId}/cancel
```

It remains separate from legacy `/api/agent/confirm`.

## 7. Pre-deployment Verification

Commands run before deployment:

- `git status --short`: clean
- `git diff --check`: passed
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`: passed, 373 tests
- `npm --prefix life-agent-web run lint`: passed
- `gcloud config get-value project`: `copper-affinity-467409-k7`
- read-only Cloud Run env describe for API and Web: passed

## 8. Deployment Commands

API:

```bash
gcloud run deploy life-agent-api \
  --source ./LifeAgent.Api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --quiet
```

Web:

```bash
gcloud run deploy life-agent-web \
  --source ./life-agent-web \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --quiet
```

No env mutation flags were used.

## 9. Smoke Results

Completed smoke:

| Check | Result |
| --- | --- |
| API service latest ready | pass |
| API traffic on new revision | pass |
| Web service latest ready | pass |
| Web traffic on new revision | pass |
| API `/health` | pass, HTTP 200 |
| Web Cloud Run URL | pass, HTTP 200 |
| `life.zhuchaofan.com` | pass, HTTP 200 |
| Demo API without auth | pass, HTTP 401 |
| Dangerous write flag combo after deploy | pass, not present |

Not completed:

- authenticated Agent Preview UI smoke
- authenticated Phase 8 create / confirm / cancel smoke
- browser-level verification of `executed=false`, `wroteData=false`,
  `legacyConfirm=false`, and `realWritePath=false`

Reason:

- no Firebase ID token was available in this execution environment
- results were not fabricated

## 10. Authenticated Smoke Status

Authenticated smoke: **pending**.

Required follow-up:

1. login at `https://life.zhuchaofan.com/`
2. open Agent Preview
3. generate Phase 8 demo pending action
4. confirm and verify:
   - status is `confirmed`
   - `executed=false`
   - `wroteData=false`
   - `legacyConfirm=false`
   - `realWritePath=false`
5. generate or reuse another pending action and cancel it
6. confirm no legacy `/api/agent/confirm` request is used for the demo path

## 11. Rollback

Rollback performed: no.

Reason:

- deployment succeeded
- service readiness smoke passed
- Cloud Run env remained safe
- no dangerous write flag combination was found

Rollback plan if later authenticated smoke fails:

- route traffic back to `life-agent-api-00039-w2d`
- route traffic back to `life-agent-web-00018-bpq`
- re-confirm write flags remain disabled
- no data rollback expected for Phase 8 demo because it is in-memory and does
  not persist real data

## 12. Final Conclusion

Current conclusion: **deployed with authenticated smoke pending**.

The preview-only Cloud Run deployment is live at service level and preserves the
expected safety boundary. The remaining validation is authenticated UI smoke for
the Phase 8 demo confirmation loop.

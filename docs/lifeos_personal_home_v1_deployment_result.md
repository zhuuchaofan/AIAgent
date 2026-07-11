# LifeOS Personal Home v1 Deployment Result

Date: 2026-07-11

## Executive Summary

LifeOS Personal Home v1 was deployed to the Cloud Run web preview service.

Current conclusion: **deployed successfully with manual authenticated UI smoke
pending**.

The deployment was Web-only. The API service was not deployed.

## Deployment Scope

- service: `life-agent-web`
- project: `copper-affinity-467409-k7`
- region: `us-central1`
- public URL: `https://life.zhuchaofan.com/`
- deployment time: `2026-07-11T08:46:16Z`

Not deployed:

- `life-agent-api`

## Pre-deployment State

Web:

- previous latest ready revision: `life-agent-web-00020-rp7`
- previous traffic: `life-agent-web-00020-rp7` at 100%
- Cloud Run URL: `https://life-agent-web-hyo2yvwwia-uc.a.run.app`

API:

- not deployed
- reason: this stage changed only frontend UI and docs

## Deployment Method

The existing `life-agent-web/deploy.sh` script was not used because it passes
`--set-env-vars` and `--set-build-env-vars`. This stage was required to avoid
Cloud Run env mutation.

Executed Web-only command shape:

```text
gcloud run deploy life-agent-web \
  --source ./life-agent-web \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --allow-unauthenticated
```

No `--set-env-vars`, `--update-env-vars`, or `--remove-env-vars` flags were
used.

## Post-deployment State

Web:

- new latest ready revision: `life-agent-web-00021-7bx`
- traffic: `life-agent-web-00021-7bx` at 100%
- Cloud Run URL: `https://life-agent-web-hyo2yvwwia-uc.a.run.app`

Web env:

- `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW=true`
- API URL still points to the existing API service
- no Web env mutation was requested

API:

- not deployed
- no API env changes

## Local Validation

Passed:

- `git status --short`
- `git diff --check`
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
  - result: 404 passed
- `npm --prefix life-agent-web run lint`
- `npm --prefix life-agent-web run build`

Build note:

- The first local build failed because the sandbox could not fetch Google Fonts
  for `next/font`.
- The same build passed after network access was allowed.

## Smoke Results

Service smoke:

- Web Cloud Run URL: HTTP 200
- `https://life.zhuchaofan.com/`: HTTP 200
- Cloud Run service ready: yes
- traffic to new revision: 100%

Authenticated UI smoke:

- status: pending
- reason: browser automation could not attach to the in-app browser during this
  deployment turn
- manual check still required:
  - login
  - verify title `LifeOS Personal Home`
  - enter `个人助手`
  - verify `创建待确认动作`
  - verify safety chips
  - confirm/cancel actions
  - refresh and verify pending action history remains visible

## Safety Result

This deployment did not:

- deploy `life-agent-api`
- modify Cloud Run env
- modify `firestore.rules`
- modify `firebase.json`
- modify package or lock files
- install dependencies
- write `memories`
- write `life_events`
- execute real tool actions
- call external provider APIs
- push git commits

## Rollback

Rollback was not executed because the Web service deployed successfully and
served 100% traffic.

Rollback option:

- shift `life-agent-web` traffic back to `life-agent-web-00020-rp7`

## Final Conclusion

LifeOS Personal Home v1 is deployed to the Web Cloud Run preview service. The
remaining validation item is a manual authenticated UI smoke to confirm the
signed-in Personal Home surface and pending action history UI are visible in the
browser.

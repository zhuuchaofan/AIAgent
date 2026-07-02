# Phase 6.8A Pre-deployment Blocker Resolution

Date: 2026-07-02

## Scope

This document records Phase 6.8A pre-deployment blocker resolution.

This task only checks and records readiness blockers:

- Cloud Run env describe pending.
- `FIREBASE_ID_TOKEN` missing.

This task does not:

- Deploy.
- Modify Cloud Run env.
- Enable durable Memory write.
- Modify Firestore Rules.
- Modify MCP.
- Push.
- Enter the next implementation stage.
- Connect a real Firestore Memory runtime.

## Baseline

Phase 6.8A pre-deployment readiness review commit:

- `a437d15bacc14ea1e799d8ecf1dd783922f1b889`

Original readiness decision:

- `PARTIAL READY - TOKEN OR ENV CHECK PENDING`

Original pending reasons:

- `FIREBASE_ID_TOKEN` missing.
- Cloud Run env describe pending.

## Git State

Command:

```bash
git status --branch --short
```

Result:

```text
## main...origin/main [ahead 8]
```

Command:

```bash
git status --short
```

Result:

```text
clean
```

Git state result:

- Working tree was clean before creating this blocker resolution document.

## Cloud Run Env Check

Read-only command executed:

```bash
gcloud run services describe life-agent-api \
  --region=us-central1 \
  --project=copper-affinity-467409-k7 \
  --format=json
```

Result summary:

- Describe command: succeeded.
- Service name: `life-agent-api`
- Region: `us-central1`
- Project: `copper-affinity-467409-k7`
- Current ready revision: `life-agent-api-00038-w9d`
- Latest created revision: `life-agent-api-00038-w9d`
- Traffic: 100% to `life-agent-api-00038-w9d`

Environment flag check:

| Env / flag | Result |
| --- | --- |
| `ENABLE_AGENT_WRITE_TOOLS` | unset / not enabled |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | unset / not enabled |
| Durable Memory write flags | absent / not enabled |
| `USE_MOCK_AUTH` | `false` |
| `USE_MOCK_LLM` | `false` |
| New Memory write flag | not present |
| Cloud Run env modified | no |

Cloud Run env decision:

- Environment is safe for preview-only deployment smoke consideration.
- No write flag is enabled.
- No mock auth or mock LLM is enabled.
- No env modification was performed.

## FIREBASE_ID_TOKEN Check

Command:

```bash
echo "$FIREBASE_ID_TOKEN" | cut -c1-40
```

Result:

- Token exists in current shell: no.
- Full token recorded: no.
- Fake token used: no.
- Mock auth enabled: no.

Impact:

- Authenticated smoke remains blocked until the user supplies a valid
  `FIREBASE_ID_TOKEN`.

## No-write Boundary

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

## Final Readiness Decision

Decision: `PARTIAL READY - FIREBASE_ID_TOKEN MISSING`

Reason:

- Cloud Run env describe succeeded.
- Cloud Run env is safe for preview-only smoke consideration.
- Write flags are not enabled.
- Mock auth and mock LLM are disabled.
- `FIREBASE_ID_TOKEN` is missing in the current shell.

## Next Step Recommendation

Because readiness is partial:

- The user must provide a valid `FIREBASE_ID_TOKEN`.
- After token is available, rerun readiness / authenticated smoke checks.
- Do not deploy from this blocker resolution step.
- Do not enable mock auth.
- Do not enable durable Memory write.

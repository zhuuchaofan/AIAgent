# Phase 8.2 Preview-only Deployment Gate Review

Date: 2026-07-09

## 1. Executive Summary

Gate decision: **CONDITIONAL GO** for Phase 8.3 Cloud Run Preview Deployment.

The local code and tests are ready for a preview-only deployment of the Phase 8
fake-first Pending Action demo. Cloud Run env was verified read-only and the API
service does not currently have the dangerous Agent write flag combination.

Conditions before deployment:

- user explicitly approves Phase 8.3 deployment
- deployment remains preview-only
- no Cloud Run env changes are made except those explicitly approved
- `ENABLE_AGENT_WRITE_TOOLS` remains unset/false
- `ENABLE_CREATE_LIFE_EVENT_TOOL` remains unset/false
- post-deployment smoke verifies `confirmed != executed`,
  `wroteData=false`, and `realWritePath=false`

No deployment was performed in this phase.

## 2. User-visible Capability

If deployed, users with Agent Preview enabled will see:

- Phase 8 Pending Action demo inside Agent Preview
- generate pending action
- confirm pending action
- cancel pending action
- status display for `pending`, `confirmed`, and `cancelled`
- `confirmed but not executed`
- `executed=false`
- `wroteData=false`
- `legacyConfirm=false`
- `realWritePath=false`
- safety mode `phase8_fake_first_in_memory`

The demo is intentionally process-memory only. It does not persist across API
instance restarts or multiple instances.

## 3. Safety Boundary

Confirmed:

- Phase 8 demo uses `/api/agent/pending-actions/demo`
- Phase 8 demo confirm uses
  `/api/agent/pending-actions/demo/{actionId}/confirm`
- Phase 8 demo cancel uses
  `/api/agent/pending-actions/demo/{actionId}/cancel`
- Phase 8 demo does not call legacy `/api/agent/confirm`
- Phase 8 demo does not call `IPendingAgentActionStore`
- Phase 8 demo does not call `FirestorePendingAgentActionStore`
- Phase 8 demo does not call `AgentLifeEventConfirmationWriteCoordinator`
- Phase 8 demo does not write `life_events`
- Phase 8 demo does not write `memories`
- Phase 8 demo does not write `users/{userId}/pendingActions`
- Phase 8 demo uses in-memory runtime
- `confirmed` does not equal `executed`
- `executionReady` remains false
- `wroteData` remains false
- `realWritePath` remains false

## 4. Legacy Confirm Risk

Legacy `/api/agent/confirm` still exists and remains the old
`PendingAgentAction` path.

Risk:

- if `ENABLE_AGENT_WRITE_TOOLS=true` and
  `ENABLE_CREATE_LIFE_EVENT_TOOL=true`, legacy `/api/agent/confirm` can enter
  the `create_life_event` write coordinator
- that legacy route is separate from the Phase 8 demo route
- Phase 8 demo does not call this legacy route

Dangerous flag combination:

```text
ENABLE_AGENT_WRITE_TOOLS=true
ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

Read-only Cloud Run env check result:

- API env was readable
- `ENABLE_AGENT_WRITE_TOOLS`: not present
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not present
- dangerous Agent write flag combination: not found

The risk is therefore controlled for preview-only deployment as long as these
flags remain unset/false.

## 5. Cloud Run Env Readiness

Project / region:

- project: `copper-affinity-467409-k7`
- region: `us-central1`

Services:

| Service | Readable | Latest ready revision observed | Traffic entries | Notes |
| --- | --- | --- | --- | --- |
| `life-agent-api` | yes | `life-agent-api-00039-w2d` | 1 | Agent write flags not present; mock auth/LLM present but false. |
| `life-agent-web` | yes | `life-agent-web-00018-bpq` | 1 | `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` present and true. |

Sanitized flag status:

| Flag | Service | Present | True | Risk |
| --- | --- | --- | --- | --- |
| `ENABLE_AGENT_WRITE_TOOLS` | API | no | no | safe |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | API | no | no | safe |
| `USE_MOCK_AUTH` | API | yes | no | safe |
| `USE_MOCK_LLM` | API | yes | no | safe |
| `ENABLE_MEMORY_PROPOSAL_RUNTIME` | API | no | no | safe |
| `ENABLE_MEMORY_PROPOSAL_GUARD` | API | no | no | safe |
| `ENABLE_MEMORY_RETRIEVAL` | API | no | no | safe |
| `ENABLE_MEMORY_CONTEXT_IN_AGENT` | API | no | no | safe |
| `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` | Web | yes | yes | expected for preview visibility |

Secret or Firebase config values were not recorded in this review.

## 6. Test Readiness

Latest local verification for Phase 8.1:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`: passed, 373 tests
- `npm --prefix life-agent-web run lint`: passed
- `git diff --check`: passed
- `git diff --cached --check`: passed before Phase 8.1 commit

Phase 8.2 should re-run:

- `git status --short`
- `git diff --stat`
- `git diff --check`
- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
- `npm --prefix life-agent-web run lint`

Deployment smoke is still required after any approved deployment.

Suggested preview-only smoke assertions after deployment:

- Agent Preview is visible only when intended
- Phase 8 demo can create a pending action
- confirm returns `confirmed`
- response/UI show `executed=false`
- response/UI show `wroteData=false`
- response/UI show `legacyConfirm=false`
- response/UI show `realWritePath=false`
- cancel returns `cancelled`
- legacy real-write flags remain disabled

## 7. Deployment Scope

If Phase 8.3 is approved, deployment scope must be preview-only.

Deployment must not enable:

- real Firestore `pendingActions` write
- durable `memories` write
- `life_events` write through Agent confirmation
- real tool execution
- external provider execution
- `ENABLE_AGENT_WRITE_TOOLS`
- `ENABLE_CREATE_LIFE_EVENT_TOOL`
- `USE_MOCK_AUTH`

Deployment should not modify `firestore.rules`, `firebase.json`, package files,
or production Firebase configuration.

## 8. Rollback Plan

If the preview deployment causes UI/API issues:

1. Shift traffic back to the previous Cloud Run revision, or redeploy the
   previous known-good revision.
2. Confirm `ENABLE_AGENT_WRITE_TOOLS` remains unset/false.
3. Confirm `ENABLE_CREATE_LIFE_EVENT_TOOL` remains unset/false.
4. Confirm `USE_MOCK_AUTH` remains false/unset.
5. Re-run `/health` and basic authenticated preview-only smoke.
6. No data rollback is expected for the Phase 8 demo because it does not write
   persistent data.

If legacy Agent Preview writes `agent_pending_actions` during smoke, treat those
as existing legacy preview records, not Phase 8 demo writes.

## 9. Final Gate Decision

Decision: **CONDITIONAL GO**.

Recommended next phase:

```text
Phase 8.3 Cloud Run Preview Deployment
```

Required user approvals before Phase 8.3:

- approve deployment
- confirm no Cloud Run write flags should be enabled
- confirm Agent Preview remaining visible with `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW`
  is acceptable
- confirm post-deployment preview-only smoke scope

## 10. Non-goals

This phase did not:

- deploy
- push
- modify runtime/API code
- modify frontend code
- modify tests
- modify Cloud Run env
- modify production config
- modify `firestore.rules`
- modify `firebase.json`
- modify Dockerfiles or deployment scripts
- install dependencies
- connect real Firestore
- create Firestore collections
- write `users/{userId}/pendingActions`
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- call external provider APIs

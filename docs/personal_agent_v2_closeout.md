# Personal Agent v2 Closeout

Date: 2026-07-11

## Executive Summary

Personal Agent v2 is complete for the main user-visible state-memory path:
logged-in users can create pending actions, refresh and restore history, confirm
or cancel actions, and see that confirmed actions are not executed.

The system remains preview-only for action execution. It writes only pending
action state under:

```text
users/{userId}/pendingActions/{pendingActionId}
```

It does not write `memories`, does not write `life_events`, and does not execute
real tools.

## Completed Capability

- User login through the existing Firebase Auth path.
- Agent Preview visible in production.
- Pending action creation through `/api/agent/pending-actions`.
- Firestore-backed pending action persistence.
- Refresh restores pending action history.
- Confirmed status persists after refresh.
- Cancelled status persists after refresh.
- Confirmed and cancelled history remains visible.
- Terminal actions do not expose active confirm/cancel controls.
- UI shows preview-only safety metadata:
  - `executed=false`
  - `wroteData=false`
  - `legacyConfirm=false`
  - `realWritePath=false`
  - `mode=personal_agent_v2_firestore_persistence_preview_only`

## Architecture

Main route:

```text
AgentPreview.tsx
  -> /api/agent/pending-actions
  -> Phase80PendingActionRuntime
  -> IPendingActionStore
  -> PendingActionStoreFactory
  -> FirestorePendingActionStore
  -> users/{userId}/pendingActions/{pendingActionId}
```

Rollback route:

```text
PendingActionStoreFactory
  -> InMemoryPendingActionStore
```

Legacy route:

```text
/api/agent/confirm
  -> IPendingAgentActionStore
  -> legacy PendingAgentAction
```

The legacy confirm route remains separate and is not used by Personal Agent v2.

## Deployment Evidence

API:

- service: `life-agent-api`
- project: `copper-affinity-467409-k7`
- region: `us-central1`
- revision: `life-agent-api-00042-zhr`
- traffic: 100%

Web:

- service: `life-agent-web`
- revision: `life-agent-web-00020-rp7`
- traffic: 100%
- domain: `https://life.zhuchaofan.com/`

Enabled API env:

```text
AGENT_PENDING_ACTION_STORE_MODE=firestore
AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true
AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true
```

Dangerous write flags:

- `ENABLE_AGENT_WRITE_TOOLS`: not set
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set

Mock flags:

- `USE_MOCK_AUTH=false`
- `USE_MOCK_LLM=false`

## Verification Evidence

Local verification:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`
  - latest recorded result: 404 passed
- frontend lint passed during rollout
- frontend production build passed during rollout after network access was
  allowed for Google Fonts

Service smoke:

- API `/health`: HTTP 200
- unauthenticated pending action API: HTTP 401
- `https://life.zhuchaofan.com/`: HTTP 200

User-verified authenticated smoke:

- user logged in to `https://life.zhuchaofan.com/`
- user created pending actions
- user confirmed one action
- user cancelled one action
- user refreshed the page
- confirmed and cancelled actions remained visible after refresh
- UI showed the expected preview-only safety flags as false

## Owner Isolation

Current evidence:

- endpoint owner comes from authenticated security context, not request body
- store path is scoped by owner:
  `users/{userId}/pendingActions/{pendingActionId}`
- in-memory fallback uses `(userSubjectRef, pendingActionId)` owner-scoped keys
- local tests cover:
  - cross-user access blocked
  - endpoint cross-user confirmation returns 404
  - same `pendingActionId` across users cannot cross-mutate records

Remaining optional smoke:

- deployed cross-user owner-isolation smoke with a second Firebase test user

This is recommended before broadening Agent capabilities, but it is not blocking
the Personal Agent v2 main user-visible closeout because the code path and local
tests prove the ownership boundary and the deployed main path is authenticated
and persistent.

## Safety Boundaries

Still disabled:

- real tool execution
- `life_events` write path through Personal Agent v2
- `memories` write path through Personal Agent v2
- external provider execution
- frontend direct Firestore access

Confirmed does not mean executed:

- `confirmed` is only a durable state-memory transition
- `executed` remains false
- `wroteData` remains false
- execution remains a future gated runtime

## Rollback

Safe rollback options:

- set `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=false`
- or set `AGENT_PENDING_ACTION_STORE_MODE=in_memory`
- or set `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=false`
- or shift API traffic back to `life-agent-api-00041-w2n`

Rollback must keep dangerous write flags unset or false.

## Closeout Decision

Personal Agent v2 is closed out for:

- personal pending action state memory
- preview-only confirmation lifecycle
- persistent history restore
- no real execution
- no business data write

Next development should move to Phase 6 Memory Engine planning and
implementation, while keeping memory writes behind their own explicit guard and
release gate.

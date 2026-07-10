# Phase 9.1 Firestore Persistence Release Gate Review

Date: 2026-07-10

## Executive Summary

Gate decision: **CONDITIONAL GO for a future preview-only Firestore persistence
enablement, after explicit user approval.**

This review did not enable Firestore persistence, did not change Cloud Run env,
did not deploy, and did not write real Firestore data. The current production
services remain preview-only and safe by default.

## Scope

This gate review covers only Personal Agent v2 pending action persistence:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Out of scope:

- `life_events`
- `memories`
- real tool execution
- external provider execution
- Firestore Rules deployment
- Cloud Run env mutation
- Cloud Run deployment

## Current Cloud Run State

Project:

```text
copper-affinity-467409-k7
```

Region:

```text
us-central1
```

API service:

```text
life-agent-api
latestReadyRevisionName: life-agent-api-00041-w2n
traffic: 100% -> life-agent-api-00041-w2n
service account: 151587524132-compute@developer.gserviceaccount.com
```

Web service:

```text
life-agent-web
latestReadyRevisionName: life-agent-web-00020-rp7
traffic: 100% -> life-agent-web-00020-rp7
service account: 151587524132-compute@developer.gserviceaccount.com
```

## Current Env Readiness

API critical flags:

| Flag | Current state | Gate meaning |
| --- | --- | --- |
| `ENABLE_AGENT_WRITE_TOOLS` | not set | safe |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | not set | safe |
| `USE_MOCK_AUTH` | set non-true | safe |
| `USE_MOCK_LLM` | set non-true | safe |
| `AGENT_PENDING_ACTION_STORE_MODE` | not set | currently in-memory |
| `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE` | not set | Firestore persistence disabled |
| `AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY` | not set | code default is preview-only |

Web critical flags:

| Flag | Current state | Gate meaning |
| --- | --- | --- |
| `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` | true | Agent Preview visible |
| `API_BASE_URL` | set | Web can call API |

Dangerous write flag combination:

```text
ENABLE_AGENT_WRITE_TOOLS=true AND ENABLE_CREATE_LIFE_EVENT_TOOL=true
```

Current result:

```text
false
```

## IAM Readiness

The API and Web services currently use:

```text
151587524132-compute@developer.gserviceaccount.com
```

Observed project-level roles for this service account:

- `roles/artifactregistry.writer`
- `roles/editor`
- `roles/iam.serviceAccountUser`
- `roles/logging.logWriter`
- `roles/run.admin`

Gate risk:

- `roles/editor` is broader than the minimum needed for Personal Agent v2
  pending action persistence.
- This does not block the code path from being preview-only, but it should be
  explicitly accepted or mitigated before enabling durable Firestore
  persistence.

Recommended mitigation before or during production hardening:

- use a narrower API runtime service account
- grant only the Firestore permissions required for
  `users/{userId}/pendingActions`
- avoid broad permissions for `life_events`, `memories`, Cloud Storage writes,
  or tool execution infrastructure

## Code Readiness

Current local code provides:

- `IPendingActionStore` as the mainline pending action contract
- `InMemoryPendingActionStore` as the safe default
- `FirestorePendingActionStore` as the durable candidate
- `PendingActionStoreFactory` for mode selection
- `PendingActionPersistenceOptions` for explicit Firestore enablement
- `PendingActionTransitionPolicy` for shared transition safety
- Firestore transaction-backed status and metadata mutations in the candidate
  store, preserving owner checks and preventing concurrent confirm/cancel stale
  writes from replacing the latest state
- `/api/agent/pending-actions` as the Personal Agent v2 route
- persistence metadata in the list response
- UI display of current persistence state

Current code does not:

- enable Firestore persistence by default
- write `life_events`
- write `memories`
- execute tools
- call external providers

## Required Enablement Env

Future approved enablement should change only these pending-action persistence
flags:

```text
AGENT_PENDING_ACTION_STORE_MODE=firestore
AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=true
AGENT_PENDING_ACTION_STORE_PREVIEW_ONLY=true
```

The following must remain unset or false:

```text
ENABLE_AGENT_WRITE_TOOLS
ENABLE_CREATE_LIFE_EVENT_TOOL
```

## Required Smoke After Approval

After explicit approval, env change, and preview deployment:

1. Confirm `/health` is healthy.
2. Confirm UI/API reports `firestorePersistenceEnabled=true`.
3. Log in with a real Firebase user.
4. Create a pending action.
5. Refresh the page and verify the pending action remains visible.
6. Confirm the action and refresh again.
7. Cancel a separate action and refresh again.
8. Confirm cross-user access is blocked if a second test user is available.
9. Confirm all action views still show:
   - `executed=false`
   - `wroteData=false`
   - `legacyConfirm=false`
   - `realWritePath=false`
10. Confirm no `life_events` or `memories` writes occurred.

## Rollback Plan

Safe rollback options:

1. Set `AGENT_PENDING_ACTION_STORE_ALLOW_FIRESTORE=false`.
2. Or set `AGENT_PENDING_ACTION_STORE_MODE=in_memory`.
3. Or shift Cloud Run traffic back to the previous revision.

Expected rollback impact:

- Pending action records already created in Firestore may remain as preview-only
  records.
- No `life_events` or `memories` rollback should be needed because this gate
  does not enable real business-data writes.

## Final Gate Decision

Decision:

```text
CONDITIONAL GO
```

Conditions:

1. User explicitly approves Cloud Run env change.
2. User explicitly approves preview deployment.
3. Dangerous legacy write flags remain unset or false.
4. User accepts current broad service-account IAM risk or approves a service
   account hardening step first.
5. Authenticated smoke must be run immediately after deployment.

Until those conditions are met, Personal Agent v2 remains locally ready but not
production complete.

# Phase 4.8 Create Life Event Write Path Closeout

## Current Default Production Behavior

Phase 4.8 keeps production safe by default.

Required flags:

```text
ENABLE_AGENT_WRITE_TOOLS=false
ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

When either flag is false:

- `/api/agent/confirm` remains preview-only for `create_life_event`.
- The pending action can complete its preview confirmation lifecycle.
- `previewOnly=true`.
- `wroteData=false`.
- No `life_event` is created.
- No Firestore `users/{userId}/life_events/{eventId}` write happens.
- Production does not enable real Agent writes by default.

Service registration does not mean production writing is enabled. The write path is gated by `AgentWriteFeatureGate.CanCreateLifeEvent()`.

## Flags-True Write Behavior

When both flags are true and the pending action type is exactly `create_life_event`, `/api/agent/confirm` may enter:

```text
AgentLifeEventConfirmationWriteCoordinator
```

The write path is:

```text
/api/agent/confirm
  -> read authenticated userId from backend context
  -> load pending action under users/{userId}/agent_pending_actions/{actionId}
  -> require actionType=create_life_event
  -> require AgentWriteFeatureGate.CanCreateLifeEvent()
  -> map payload with LifeEventActionPayloadMapper
  -> validate payload with LifeEventPayloadValidator
  -> create life_event through IAgentLifeEventService
  -> write users/{userId}/life_events/{eventId}
  -> after write success, mark pending action confirmed
  -> persist created resource result fields
```

Successful result semantics:

```json
{
  "previewOnly": false,
  "wroteData": true,
  "createdResourceType": "life_event",
  "createdResourceId": "evt_<agentActionId>",
  "actionType": "create_life_event",
  "idempotent": false
}
```

After successful write:

- Pending action status is `confirmed`.
- `confirmedAt` is set.
- `createdResourceType=life_event`.
- `createdResourceId` is stored.
- `wroteData=true`.
- `writeCompleted=true`.
- `writeCompletedAt` is set.

## Feature Gate Protection

`AgentWriteFeatureGate.CanCreateLifeEvent()` is the required precondition for real writes.

Rules:

- `ENABLE_AGENT_WRITE_TOOLS=true` alone is not enough.
- `ENABLE_CREATE_LIFE_EVENT_TOOL=true` alone is not enough.
- Both flags must be true.
- Non-`create_life_event` actions never enter the LifeEvent write coordinator.
- `create_life_event_preview` remains preview-only compatibility behavior.
- Registered writer/service/coordinator dependencies cannot bypass the feature gate.

Default production remains safe because the flags are false.

## Idempotency Strategy

LifeEvent id is derived from the pending action id:

```text
evt_{agentActionId}
```

The pending action stores created resource information:

```text
createdResourceType
createdResourceId
wroteData
writeCompleted
writeCompletedAt
```

Duplicate confirm behavior:

- Repeated confirm does not call the writer again.
- Repeated confirm returns the same `createdResourceId`.
- Response includes `idempotent=true`.
- The original created `life_event` remains the single result for that action.

This makes retry safe when the first write succeeded but the client did not receive the response.

## Write Failure Strategy

If `life_event` writing fails:

- Response status is `write_failed`.
- `wroteData=false`.
- Pending action remains `pending`.
- `confirmedAt` is not set.
- `createdResourceId` is not set.
- `writeCompleted=false`.
- The user can retry confirm later.

The pending action is only marked `confirmed` after the `life_event` write succeeds.

## No-Write Scenarios

The system must not write a `life_event` for:

- Flags false.
- Non-`create_life_event` action types.
- `create_life_event_preview`.
- Invalid payload.
- Payload containing forbidden fields such as `userId`, `id`, `source`, `createdBy`, `agentActionId`, timestamps, `token`, `secret`, `internalPath`, or `firestorePath`.
- Cancelled action.
- Expired action.
- Cross-user action.
- Unknown action id.
- Write gate not enabled.
- Firestore/write service failure.

## Pending Action Lifecycle

Preview-only path:

```text
pending -> confirmed
previewOnly=true
wroteData=false
```

Flags-true successful write path:

```text
pending -> write life_event -> confirmed
previewOnly=false
wroteData=true
createdResourceType=life_event
createdResourceId=evt_{agentActionId}
```

Failure path:

```text
pending -> write_failed response -> pending
```

Cancelled and expired actions remain terminal and do not write data.

## Test Closure

Current test coverage includes:

- Coordinator unit tests.
- Confirm endpoint tests.
- DI safety tests.
- Flags false preview-only tests.
- Flags true write path tests.
- Duplicate confirm idempotency tests.
- Write failure tests.
- Invalid payload no-write tests.
- Cancelled action no-write tests.
- Expired action no-write tests.
- Cross-user no-write tests.
- Existing Agent/RAG/list document tests.

Key test files:

- `LifeAgent.Tests/AgentLifeEventConfirmationWriteCoordinatorTest.cs`
- `LifeAgent.Tests/AgentSkeletonTest.cs`
- `LifeAgent.Tests/AgentLifeEventDiTest.cs`
- `LifeAgent.Tests/AgentLifeEventPayloadMapperTest.cs`
- `LifeAgent.Tests/AgentLifeEventSkeletonTest.cs`
- `LifeAgent.Tests/FirestoreAgentLifeEventServiceTest.cs`

Required local gate:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
git diff --check
git status --short
git diff --stat
```

## Production Enablement Prerequisites

The following are future phases and are not part of Phase 4.8:

- Explicitly set Cloud Run feature flags.
- Update and verify Firestore Rules.
- Reconfirm Firebase Auth and user boundary behavior.
- Run authenticated online smoke tests.
- Decide frontend UI display/edit behavior for created life events.
- Define production rollback and cleanup procedures.
- Plan staged rollout or gray release.

## Phase 4.8 Boundaries

Phase 4.8 does not:

- Deploy.
- Push.
- Open production writes.
- Modify Cloud Run env.
- Modify Firestore Rules.
- Modify Firebase Auth.
- Modify frontend UI.
- Implement reminder writes.
- Implement memory writes.
- Connect Google Calendar.
- Connect MCP.
- Introduce multi-agent behavior.

Phase 4.8 ends with a feature-gated `create_life_event` write path that is tested and documented, but production remains preview-only until a later explicit enablement phase.

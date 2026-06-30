# Phase 4.8.5 Confirm Life Event Write Plan

## Scope

Phase 4.8.5 is a pre-integration safety review for connecting confirmed Agent actions to `create_life_event`.

This phase does not implement the `/api/agent/confirm` write path. It does not enable real production writes, register production write services, deploy, change Cloud Run env, change Firestore Rules, change Firebase Auth, or modify the frontend.

Current prerequisites:

- `FirestorePendingAgentActionStore`
- `AgentProposedAction`
- `PendingAgentAction`
- `AgentWriteFeatureGate`
- `LifeEventActionPayloadMapper`
- `FirestoreAgentLifeEventService`
- `FirestoreAgentLifeEventWriter`
- `AgentLifeEventFactory`

## Recommended Confirm Order

Future `/api/agent/confirm` flow for `create_life_event`:

```text
/api/agent/confirm
  -> read authenticated userId from HttpContext.Items["userId"]
  -> read pending action from users/{userId}/agent_pending_actions/{actionId}
  -> verify action exists
  -> verify action belongs to authenticated userId
  -> verify status=pending
  -> verify action is not expired
  -> verify actionType=create_life_event
  -> check AgentWriteFeatureGate
  -> flags off: keep Phase 4.7 preview-only semantics
  -> flags on: map payload with LifeEventActionPayloadMapper
  -> validate CreateLifeEventRequest with LifeEventPayloadValidator
  -> create life event under users/{userId}/life_events/{eventId}
  -> only after successful write, mark action confirmed
  -> persist createdResourceType=life_event and createdResourceId
  -> return wroteData=true + createdResourceId
```

Critical ordering rule:

- Do not mark the pending action `confirmed` before the life event write succeeds.
- If mapping, validation, or Firestore write fails, the action must remain unconfirmed.

## Feature Flag Behavior

Flags:

```text
ENABLE_AGENT_WRITE_TOOLS=false
ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

Decision matrix:

| ENABLE_AGENT_WRITE_TOOLS | ENABLE_CREATE_LIFE_EVENT_TOOL | Behavior |
|---|---|---|
| false | false | Preview-only; no life event write |
| false | true | Preview-only; no life event write |
| true | false | Preview-only; no life event write |
| true | true | Allow entering real `create_life_event` write path |

Feature flag off behavior must remain compatible with Phase 4.7:

- It may mark the action `confirmed` as a preview confirmation.
- It must return `previewOnly=true`.
- It must return `wroteData=false`.
- It must not create a `life_event`.
- It must not call `FirestoreAgentLifeEventService`.
- Read-only Agent and RAG Chat must be unaffected.

When both flags are true:

- The system may enter the real write path, but only after all ownership, lifecycle, action type, and payload checks pass.
- This still must not let payload or request body control `userId` or system fields.

## Idempotency Design

Confirmed action repeated confirm:

- A second confirm for the same `actionId` must not create another life event.
- If the pending action already has `createdResourceType=life_event` and `createdResourceId`, return that existing result.
- The response should identify the operation as idempotent.

Recommended pending action result fields:

```text
createdResourceType
createdResourceId
wroteData
previewOnly
writeCompleted
writeCompletedAt
writeErrorCode
writeErrorMessage
```

Recommended behavior after first write succeeds but response fails before client receives it:

- Because `createdResourceId` is stored on the pending action, retrying confirm can return the stored result without creating a duplicate.
- The write and pending action result update should be in one Firestore transaction when possible.
- If a single transaction is not practical, use a deterministic idempotency key:
  - event id derived from action id, for example `evt_agent_{actionIdSuffix}`, or
  - pending action stores a reserved `eventId` before write and retries use the same id.

State recommendation:

- Keep terminal state `confirmed` for completed writes.
- Add result fields rather than adding a new state for successful writes.
- Do not mark `confirmed` when write fails.
- For validation failures, keep `pending` or add a later explicit `validation_failed` terminal state only after a separate schema-state review.

## Error Scenarios

| Scenario | Expected behavior |
|---|---|
| Unknown `actionId` | `success=false`, `status=not_found`, no write |
| Action belongs to another user | `success=false`, `status=not_found`, no write |
| Action already cancelled | `success=false`, `status=cancelled`, no write |
| Action already expired | `success=false`, `status=expired`, no write |
| Action already confirmed with created resource | `success=true`, `status=confirmed`, return existing created resource, no duplicate write |
| Action already confirmed preview-only | Return existing confirmed preview result; no write |
| `actionType` is not `create_life_event` | `success=false`, `status=invalid_action_type`, no write |
| Payload contains forbidden fields | `success=false`, validation error, no write |
| Payload schema invalid | `success=false`, validation error, no write |
| Feature flag off | Preview-only confirmation, `wroteData=false`, no write |
| Firestore write fails | `success=false`, write error; action remains unconfirmed |

## Return Structure

Future `AgentConfirmationResponse.Result` should include:

```json
{
  "previewOnly": false,
  "wroteData": true,
  "createdResourceType": "life_event",
  "createdResourceId": "evt_...",
  "actionType": "create_life_event",
  "idempotent": false
}
```

For preview-only:

```json
{
  "previewOnly": true,
  "wroteData": false,
  "createdResourceType": null,
  "createdResourceId": null,
  "actionType": "create_life_event",
  "idempotent": false
}
```

For failures, recommended top-level fields:

```text
success=false
status=<lifecycle_or_error_status>
lifecycleStatus=<current_action_lifecycle>
message=<human_safe_summary>
errorCode=<machine_readable_code>
```

Do not change the API response shape until the implementation phase. This document only freezes the intended semantics.

## Security Rules

- `userId` must come only from authenticated backend context.
- Request body `UserId` must not affect authorization or write path.
- Payload `userId`, `id`, `source`, `createdBy`, `agentActionId`, timestamps, tokens, secrets, and Firestore paths must fail closed.
- `LifeEventActionPayloadMapper` must run before the write service.
- `LifeEventPayloadValidator` must run before the write service.
- `FirestoreAgentLifeEventService` must receive `userId` and `agentActionId` from backend-confirmed context, not payload.
- `FirestoreAgentLifeEventWriter` must write only to `users/{userId}/life_events/{eventId}`.

## Test Plan Before Real Hookup

Required implementation tests for the future confirm integration:

- Feature flag off returns `previewOnly=true`, `wroteData=false`, and does not call write service.
- Both flags true with valid pending action writes exactly one life event.
- Repeated confirm returns stored `createdResourceId` and does not call write service again.
- Cancelled action cannot write.
- Expired action cannot write.
- Wrong action type cannot write.
- Payload with forbidden top-level fields cannot write.
- Payload with nested forbidden structuredData fields cannot write.
- Firestore write failure leaves pending action unconfirmed.
- Request body `UserId` is ignored.
- Cross-user action returns `not_found`.
- Existing read-only Agent tests still pass.
- Existing RAG Chat tests still pass.
- Existing mapper, feature gate, and service skeleton tests still pass.

## Smoke Plan

Default production smoke while flags are off:

- `/api/agent/run` can return a `create_life_event` or preview action proposal.
- `/api/agent/confirm` returns preview-only success.
- Smoke asserts `previewOnly=true`.
- Smoke asserts `wroteData=false`.
- Smoke does not check for created life events.

Future controlled smoke with flags on:

- Use a dedicated test user.
- Create a `create_life_event` pending action.
- Confirm it.
- Assert `wroteData=true`.
- Assert `createdResourceType=life_event`.
- Assert `createdResourceId` exists.
- Confirm the same action again.
- Assert the same `createdResourceId` is returned.
- Manually clean test data only when explicitly requested.

## Rollback

Primary rollback:

- Set `ENABLE_AGENT_WRITE_TOOLS=false`, or
- Set `ENABLE_CREATE_LIFE_EVENT_TOOL=false`.

Expected rollback:

- Stops real Agent life event writes.
- Keeps read-only Agent available.
- Keeps RAG Chat available.
- Keeps existing created `life_events`; rollback does not delete data.
- Does not require Firestore Rules or frontend rollback.

If the code path is faulty:

- Roll API traffic back to the previous Cloud Run revision.
- Do not deploy Web unless UI compatibility is broken.

## Recommendation

Do not enter real confirm hookup until this plan is accepted.

Recommended next phase:

- Phase 4.8.6 should implement confirm integration behind feature flags in tests first.
- Production flags should remain false.
- Deployment should happen only after local tests prove preview-only behavior is preserved with flags off.

# Phase 4.8 Create Life Event Design

## 1. Phase 4.8 Goal

Phase 4.8 is a design freeze for the first low-risk real Agent write tool: `create_life_event`.

This document does not implement real writes. It defines the API boundary, data model, permission model, test plan, and rollback strategy for a later implementation step.

The target behavior, when explicitly enabled, is:

- The Agent may propose a minimal `life_event`.
- The Agent must not write automatically.
- The write must be triggered by user confirmation.
- The backend must derive `userId` from the authenticated request context.
- Request body, pending action payload, and model output must not decide `userId`.
- The backend must validate the payload schema before writing.
- The result must be auditable from both the pending action and created event.
- Real writes must be guarded by feature flags.
- Default behavior remains preview-only until the flags are explicitly enabled.

Phase 4.8 only covers `create_life_event`.

Non-goals:

- Do not create reminders.
- Do not create calendar events.
- Do not write long-term memory.
- Do not execute without user confirmation.
- Do not connect MCP.
- Do not introduce multi-agent orchestration.
- Do not change Firebase Auth, Firestore Rules, Cloud Run env, RAG Chat, or AgentPreview UI in this design step.

## 2. Current Structure To Reuse

Current Agent flow:

- `AgentRunner` plans read-only RAG/document tools or creates a write-like `proposedAction`.
- `AgentEndpoints` exposes `/api/agent/run` and `/api/agent/confirm`.
- `FirestorePendingAgentActionStore` persists pending actions under `users/{userId}/agent_pending_actions/{actionId}`.
- `PendingAgentAction` tracks `userId`, `status`, `previewOnly`, `createdAt`, `updatedAt`, `confirmedAt`, `cancelledAt`, and `expiredAt`.
- `AgentProposedAction` carries `actionId`, `actionType`, `title`, `summary`, `payload`, risk, lifecycle status, and expiry.
- `AgentPreview` already renders a confirmation card and calls `confirmAgentAction(actionId, decision)`.

Current Firestore user-path conventions:

- Documents: `users/{userId}/documents/{documentId}`.
- Chat sessions: `users/{userId}/chat_sessions/{sessionId}` and nested `messages`.
- Pending Agent actions: `users/{userId}/agent_pending_actions/{actionId}`.
- Life events already use `users/{userId}/life_events/{eventId}` through `LifeEventService`.

Current LifeEvent behavior:

- `LifeEvent` already has Firestore annotations and system fields.
- `LifeEventService.SaveEventAsync(userId, lifeEvent)` writes to `users/{userId}/life_events/{eventId}`.
- The service currently generates `eventId`, overwrites `UserId`, sets server timestamps, and writes through the authenticated `userId` argument.
- `LifeEventSchemaValidator` exists for stronger schema validation in the ingest path.

Current Phase 4.7 boundary:

- `/api/agent/confirm` is preview-only.
- It returns `previewOnly=true` and `wroteData=false`.
- No real `create_reminder`, `save_memory`, or `create_life_event` Agent tool is enabled.

## 3. Minimal LifeEvent Data Model

Phase 4.8 should write the smallest useful life event and map it onto the existing `LifeEvent` model instead of creating a parallel model.

### Frozen Data Model Boundary

`agentActionId`, `createdBy`, and `source` must be explicit `LifeEvent` fields, not values inside `structuredData`.

Reasons:

- These fields are audit, provenance, and security traceability fields.
- Later debugging and review need direct tracking of Agent-created data.
- `agentActionId` links the event to `users/{userId}/agent_pending_actions/{actionId}`.
- `createdBy` distinguishes user/manual creation from Agent creation.
- `source` distinguishes `manual`, `agent_confirmed`, `import`, and future sources.
- Putting these fields inside `structuredData` would weaken queryability, indexing, auditing, and incident debugging.

Frozen minimal event fields:

```text
id
userId
type
title
content
source
createdBy
agentActionId
createdAt
updatedAt
structuredData
```

Mapping to the current `LifeEvent` model:

| Design field | Current model field | Source of truth |
|---|---|---|
| `id` | `Id` | Backend generated |
| `userId` | `UserId` | Backend authenticated context |
| `type` | `Type` | Agent proposed, backend validated |
| `title` | `Title` | Agent proposed, backend validated |
| `content` | `Content` | Agent proposed, backend validated |
| `source` | `Source` | Backend overwritten to `agent_confirmed` |
| `createdBy` | New explicit field | Backend overwritten to `agent` |
| `agentActionId` | New explicit field | Backend overwritten from confirmed pending action |
| `createdAt` | `CreatedAt` | Backend server time |
| `updatedAt` | `UpdatedAt` | Backend server time |
| `structuredData` | `StructuredData` | Agent proposed low-risk business extension data, backend allowlist-filtered |

Explicit fields:

- `id`
- `userId`
- `type`
- `title`
- `content`
- `source`
- `createdBy`
- `agentActionId`
- `createdAt`
- `updatedAt`

`structuredData` is only for low-risk business extension information, for example:

- `tags`
- `catName`
- `mood`
- `importance`
- `locationLabel`
- `rawExtractedHints`

Backend must always overwrite these fields:

- `Id`
- `UserId`
- `Source`
- `CreatedBy`
- `AgentActionId`
- `CreatedAt`
- `UpdatedAt`
- `OccurredAt` unless a later phase deliberately supports validated occurrence time.
- Any audit fields linking the event to the confirmation.

Agent may propose only these business fields:

- `type`
- `title`
- `content`
- `tags`
- `importance`
- `metadata` or `structuredData` low-risk keys.

Field source rules:

- `userId`: generated only from the backend authenticated context; forbidden from request body, payload, and LLM output.
- `id`: backend generated.
- `source`: backend generated; for Agent confirmed writes, use `agent_confirmed`.
- `createdBy`: backend generated; for Agent confirmed writes, use `agent`.
- `agentActionId`: backend-bound from the confirmed pending action.
- `createdAt` / `updatedAt`: backend generated.
- `type` / `title` / `content`: may come from `proposedAction.payload`, but confirm must validate schema and enforce length limits.
- `structuredData`: may come from `proposedAction.payload`, but must be allowlist-filtered. It must not carry `userId`, system fields, tokens, secrets, internal paths, Firestore document paths, or auth-related data.

For Phase 4.8, recommended defaults:

- `source = "agent_confirmed"`
- `createdBy = "agent"`
- `schemaVersion = "v1"`
- `needsReview = false` only if schema validation succeeds; otherwise reject instead of writing.
- `reminderIntentDetected = false`
- `reminderParseStatus = "none"`
- `createdReminderId = null`

Do not let Agent payload set system fields, Firestore paths, or `userId`.

## 4. Firestore Path

Recommended write path:

```text
users/{userId}/life_events/{eventId}
```

Reasons:

- It matches the current `LifeEventService` implementation.
- It follows existing user-isolated subcollection conventions for documents, chat sessions, reminders, and pending Agent actions.
- It keeps authorization simple: all writes happen below the authenticated user's document.
- It avoids trusting frontend or model-provided tenant/path data.

Cross-user protection:

- `/api/agent/confirm` reads `userId` from `HttpContext.Items["userId"]`.
- The pending action lookup must use `users/{authenticatedUserId}/agent_pending_actions/{actionId}`.
- The life event write must use `users/{authenticatedUserId}/life_events/{eventId}`.
- Request body `UserId`, pending action payload `userId`, and model output `userId` must be ignored or rejected.

Collection group:

- Not needed for Phase 4.8.
- User timeline and list APIs can continue querying within one user's `life_events` subcollection.
- Collection group may be revisited later for admin analytics or cross-user maintenance jobs, but it is not part of the Agent write path.

Indexes:

- No new index is required for the write itself.
- Existing timeline queries may already require indexes based on `isDeleted`, `occurredAt`, `type`, or tags.
- Phase 4.8 should not add indexes unless a concrete query fails during implementation.

## 5. ProposedAction Payload

The proposed action payload should be a business proposal only.

Recommended action type:

```text
create_life_event
```

During feature-flag-off preview compatibility, the system may continue producing `create_life_event_preview`. When the tool is enabled, the durable action type should be explicit and non-preview: `create_life_event`.

Example payload:

```json
{
  "type": "cat",
  "title": "观察黑猫状态",
  "content": "明天提醒我观察黑猫",
  "tags": ["猫", "健康"],
  "importance": 2,
  "metadata": {
    "animal": "黑猫",
    "intent": "observation"
  }
}
```

Rules:

- Payload is only a proposal.
- Confirm must reload the persisted pending action and validate payload server-side.
- Payload must not carry `userId`.
- Payload must not carry `id`, `createdAt`, `updatedAt`, `deletedAt`, Firestore path, `source`, `createdBy`, `agentActionId`, or `createdReminderId`.
- If forbidden fields are present, the backend should ignore them only when harmless, but prefer rejecting payloads that include authority-bearing fields such as `userId` or path-like values.
- Payload must be size-limited to avoid storing large arbitrary text.
- `type` must be one of the allowed current `LifeEvent` types: `cycling`, `home`, `cat`, `life`, or `unknown`.
- `importance` must be clamped or validated within the existing 1-5 range.
- `tags` must be a bounded string list.

## 6. Confirm Flow

Target flow:

```text
/api/agent/run
  -> detect write intent
  -> generate proposedAction(actionType=create_life_event)
  -> save pending action in Firestore
  -> return requiresConfirmation=true
  -> AgentPreview shows confirmation card

/api/agent/confirm
  -> read authenticated userId from backend context
  -> load users/{userId}/agent_pending_actions/{actionId}
  -> verify action belongs to current user
  -> verify action is pending and not expired
  -> verify actionType=create_life_event
  -> verify feature flags allow real writes
  -> validate payload schema
  -> write users/{userId}/life_events/{eventId}
  -> mark action confirmed with audit fields
  -> return wroteData=true and createdResourceId
```

Important ordering:

1. Validate action ownership and lifecycle.
2. Validate feature flags.
3. Validate payload.
4. Write life event.
5. Mark action confirmed and record created event id.

If the Firestore event write fails, the action must not be marked `confirmed`.

Recommended transaction shape:

- Use a Firestore transaction or an equivalent idempotent two-step design.
- Read pending action.
- If already confirmed and has `createdResourceId`, return the existing result without writing again.
- If pending and valid, create the event doc with a deterministic or transaction-scoped generated id.
- Update pending action status and audit fields in the same transaction if practical.

Error scenarios:

| Scenario | Expected behavior |
|---|---|
| Action does not exist | Return `success=false`, `status=not_found`; no write |
| Action belongs to another user | Return `success=false`, `status=not_found`; no write |
| Action expired | Mark/read as `expired`; return failure; no write |
| Action already confirmed | Return existing confirmed result; do not write again |
| Action already cancelled | Return failure for confirm; no write |
| Invalid decision | Return `invalid_decision`; no write |
| Feature flag off | Return preview-only success/failure consistent with Phase 4.7; no write |
| Action type mismatch | Return `invalid_action_type`; no write |
| Payload invalid | Return validation failure; keep action pending or mark validation_failed only if state model is extended |
| Firestore write fails | Return error; do not mark confirmed |

## 7. Feature Flags

Recommended flags:

```text
ENABLE_AGENT_WRITE_TOOLS=false
ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

Rules:

- Both flags default to `false`.
- If either flag is false, confirm remains preview-only and returns `previewOnly=true`, `wroteData=false`.
- Real write requires both flags to be true.
- Flags affect only write execution. They must not affect:
  - read-only Agent document tools
  - `answer_with_rag`
  - regular RAG Chat
  - document upload/processing
  - normal life event APIs outside Agent
- Phase 4.8 design should not change Cloud Run env. Implementation should read flags from configuration with safe defaults.

Recommended response when flags are off:

```json
{
  "success": true,
  "status": "confirmed",
  "message": "Agent action preview confirmed. No data was written.",
  "result": {
    "previewOnly": true,
    "wroteData": false,
    "actionType": "create_life_event"
  }
}
```

Recommended response when flags are on and write succeeds:

```json
{
  "success": true,
  "status": "confirmed",
  "message": "Agent action confirmed and life event created.",
  "result": {
    "previewOnly": false,
    "wroteData": true,
    "createdResourceType": "life_event",
    "createdResourceId": "evt_...",
    "actionType": "create_life_event"
  }
}
```

## 8. Audit And Security

Pending action audit should record:

- `actionId`
- `userId`
- `actionType`
- `status`
- `createdAt`
- `updatedAt`
- `confirmedAt`
- `confirmedBy`
- `cancelledAt`
- `expiredAt`
- `previewOnly`
- `wroteData`
- `createdResourceType`
- `createdResourceId`
- validation error summary if rejected

Created life event audit should record:

- `agentActionId`
- `createdBy = "agent"`
- `source = "agent_confirmed"`
- `createdAt`
- `userId`

`agentActionId`, `createdBy`, and `source` are explicit fields. They must not be stored under `structuredData`.

Security requirements:

- Confirm request must not be replayable into duplicate writes.
- Confirmed actions must not write a second event on repeated confirm.
- Cancelled actions must not become confirmed later.
- Expired actions must not be confirmed.
- `userId` must come only from `FirebaseAuthMiddleware`.
- Payload `userId` must be ignored or rejected.
- Firestore physical paths must be built only from authenticated `userId` and backend-generated ids.
- Write failure must not transition the pending action to `confirmed`.

## 9. Backend Implementation Plan

### 4.8.0: Design Document

Deliverable:

- `docs/phase4_8_create_life_event_design.md`.

Acceptance:

- Design names the first real write tool.
- Design keeps default preview-only behavior.
- Design covers flags, schema, permissions, audit, tests, and rollback.
- No implementation code changes.

### 4.8.1: LifeEvent Model And Service Skeleton

Deliverable:

- Add a dedicated Agent write service boundary, for example `IAgentLifeEventWriteService`.
- Reuse existing `LifeEvent` and `LifeEventService` path conventions.
- Add explicit audit field support if needed.

Acceptance:

- No Agent confirm path writes data yet.
- `userId` is accepted only as a backend method argument.
- Tests prove service skeleton rejects missing user id and overwrites system fields.

### 4.8.2: create_life_event Payload Schema Validation

Deliverable:

- Add a strict payload DTO and validator.
- Reject or strip forbidden fields.
- Validate type, title, content, tags, importance, and metadata size.

Acceptance:

- Payload with `userId` cannot influence stored `UserId`.
- Invalid payload prevents write.
- Existing preview actions remain compatible.

### 4.8.3: Feature Flag Integration

Deliverable:

- Read `ENABLE_AGENT_WRITE_TOOLS` and `ENABLE_CREATE_LIFE_EVENT_TOOL` from configuration.
- Defaults are false.

Acceptance:

- With flags false, confirm remains Phase 4.7 preview-only.
- Read-only Agent tools and RAG Chat are unaffected.

### 4.8.4: Confirm-Time Real Write

Deliverable:

- Add confirm-time executor for `create_life_event`.
- Transactionally write life event and mark pending action confirmed.

Acceptance:

- Write only occurs after user confirm.
- Repeated confirm returns existing created event id without duplicate writes.
- Write failure leaves action unconfirmed.

### 4.8.5: Tests And Smoke

Deliverable:

- Unit/integration tests for feature flags, lifecycle, payload validation, idempotency, and cross-user safety.
- Smoke script optionally validates the feature-flag-off path by default.

Acceptance:

- Full backend tests pass.
- Frontend lint/build pass if touched.
- Smoke does not create real data unless an explicit local-only or test-only flag is provided.

### 4.8.6: Deployment And Rollback

Deliverable:

- Deploy API only when backend changes are ready.
- Keep flags false in production until manual approval.

Acceptance:

- Health check passes.
- Existing Agent Preview still works.
- No real write occurs until flags are explicitly enabled.

## 10. Test Plan

Required tests:

- Feature flag off: confirm does not write and returns `previewOnly=true`, `wroteData=false`.
- Feature flag on: confirm writes one life event and returns `wroteData=true`.
- Repeated confirm does not create duplicate life events.
- Cancel then confirm fails and does not write.
- Expired action cannot be confirmed.
- Payload carrying `userId` is ignored or rejected; stored `UserId` remains authenticated user.
- Cross-user action confirm returns `not_found` and does not write.
- Invalid payload does not write.
- Firestore write failure does not mark the action confirmed.
- Existing read-only Agent tools still pass.
- Existing RAG Chat behavior and tests still pass.
- Existing life event manual ingest path is unaffected.

Suggested additional tests:

- Confirm response includes `createdResourceType=life_event`.
- Confirm response includes `createdResourceId`.
- Pending action stores `createdResourceId`.
- Life event stores `agentActionId`.
- Feature flags default to false when unset.

## 11. Frontend Impact

Phase 4.8 should avoid changing `AgentPreview` unless the backend response shape requires minimal display.

Current `AgentPreview` already:

- Displays the proposed action.
- Shows action type, title, summary, risk, and lifecycle status.
- Sends confirm/cancel decisions.
- Displays confirmation result message.

Minimal backend result fields for future UI display:

```json
{
  "wroteData": true,
  "createdResourceType": "life_event",
  "createdResourceId": "evt_..."
}
```

No complex new UI is recommended for Phase 4.8. If needed, the existing confirmation result block can show the backend message only.

## 12. Rollback Strategy

Primary rollback:

- Set `ENABLE_CREATE_LIFE_EVENT_TOOL=false`.
- Or set `ENABLE_AGENT_WRITE_TOOLS=false`.

Expected rollback behavior:

- Real Agent writes stop immediately after API config rollout.
- Read-only Agent tools remain available.
- RAG Chat remains available.
- Existing `life_events` created by earlier confirmed actions are not automatically deleted.
- Test data cleanup, if needed, must be manual and explicit.
- Do not roll back or disable the RAG main path.

If a deployed code revision is faulty:

- Roll API traffic back to the last known good Cloud Run revision.
- Keep Web unchanged unless UI compatibility is broken.
- Do not modify Firestore Rules as an emergency workaround unless separately reviewed.

## 13. Recommendation

Recommendation: do not enable real writes yet.

Suggested next step:

- Enter Phase 4.8.1 only after accepting this frozen model boundary: `agentActionId`, `createdBy`, and `source` are explicit `LifeEvent` fields.

Implementation posture:

- Start with local-only implementation and tests.
- Keep production feature flags default false.
- Continue maintaining preview-only behavior until a separate explicit enablement step.

Questions to confirm before implementation:

- Should invalid payloads leave actions `pending`, or introduce a new terminal status such as `validation_failed` in a later phase?

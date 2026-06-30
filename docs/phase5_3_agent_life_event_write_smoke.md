# Phase 5.3 Agent Life Event Write Smoke Plan

## Scope

Phase 5.3 prepares a gated smoke path for future `create_life_event` production enablement.

This phase does not enable real writes, does not change Cloud Run env, does not deploy, and does not modify Firestore Rules, Firebase Auth, frontend code, or confirm/write business semantics.

## Smoke Script

Script:

```bash
node scripts/smoke-agent-life-event-write.mjs
```

The script is independent from the existing RAG smoke so the Agent write smoke can stay more restrictive.

## Safe Defaults

The script is safe by default:

- Without `API_BASE_URL`, it skips API smoke.
- Without `FIREBASE_ID_TOKEN`, it skips authenticated Agent flow.
- Without `RUN_AGENT_WRITE_SMOKE=true`, it does not require or execute real-write assertions.
- Without `EXPECT_AGENT_WRITE_ENABLED=true`, it does not assert `wroteData=true`.
- Default behavior verifies preview-only semantics.

Real write smoke requires all of the following:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app"
FIREBASE_ID_TOKEN="..."
RUN_AGENT_WRITE_SMOKE=true
EXPECT_AGENT_WRITE_ENABLED=true
SMOKE_TEST_PREFIX="[SMOKE TEST]"
```

`SMOKE_TEST_PREFIX` is optional and defaults to `[SMOKE TEST]`.

## Scenario 1: Flags False / Preview-Only

Input:

```text
[SMOKE TEST] 帮我记一下：今天黑猫吐了一次
```

Expected Agent run result:

- `requiresConfirmation=true`
- `proposedAction.actionType=create_life_event` or `create_life_event_preview`
- `proposedAction.actionId` is present

Expected confirm result:

- `status=confirmed`
- `lifecycleStatus=confirmed`
- `previewOnly=true`
- `wroteData=false`
- `createdResourceId` is empty
- No `life_event` is created

This is the only behavior expected while production flags remain unset or false.

## Scenario 2: Flags True / Real Write

This scenario must run only when all real-write smoke env vars are present:

```text
API_BASE_URL
FIREBASE_ID_TOKEN
RUN_AGENT_WRITE_SMOKE=true
EXPECT_AGENT_WRITE_ENABLED=true
```

Expected confirm result:

- `previewOnly=false`
- `wroteData=true`
- `createdResourceType=life_event`
- `createdResourceId=evt_{agentActionId}`

Expected Firestore result:

- A document exists at `users/{userId}/life_events/{createdResourceId}`.
- `source=agent_confirmed`.
- `createdBy=agent`.
- `agentActionId` matches the pending action.
- The document title/content use the configured smoke prefix.

Phase 5.3 does not execute this path against production because production flags remain off.

## Scenario 3: Duplicate Confirm

The script confirms the same action a second time.

Preview-only expected result:

- `status=confirmed`
- `previewOnly=true`
- `wroteData=false`
- `idempotent=true`

Real-write expected result when explicitly enabled:

- `wroteData=true`
- `createdResourceId` equals the first confirm response
- No second `life_event` is created
- `idempotent=true` or equivalent idempotent result semantics

## Scenario 4: Invalid Payload

Current public API flow does not provide a safe way for the smoke script to construct arbitrary invalid pending actions without adding test-only endpoints or mutating production internals.

Manual checklist for a controlled test environment:

- Create a pending `create_life_event` action with forbidden payload fields such as `userId`, `token`, `secret`, `internalPath`, or `firestorePath`.
- Confirm the action with write flags enabled.
- Verify response `errorCode=invalid_payload` or equivalent invalid-payload result.
- Verify no `life_event` is created.
- Verify pending action does not move to a successful write-completed state.

This remains documented as a controlled-environment checklist until a safe test fixture endpoint exists.

## Scenario 5: Rollback To Preview-Only

Rollback validation is manual in this phase.

After disabling either flag:

```text
ENABLE_AGENT_WRITE_TOOLS=false
ENABLE_CREATE_LIFE_EVENT_TOOL=false
```

Run the smoke again with a new action.

Expected result:

- `previewOnly=true`
- `wroteData=false`
- No new `life_event` is created

Phase 5.3 does not modify Cloud Run env and does not perform rollback.

## Test Data Strategy

Use a dedicated test user for real-write smoke.

Rules:

- Use `SMOKE_TEST_PREFIX="[SMOKE TEST]"`.
- Do not use real user data.
- Record every `createdResourceId`.
- Verify created documents before cleanup.
- Cleanup only documents clearly created by smoke tests.
- Do not automatically delete any non-smoke data.
- Prefer manual cleanup through Firestore console or a controlled backend admin tool.

Recommended cleanup query criteria:

- Path: `users/{testUserId}/life_events/{eventId}`
- `eventId` starts with `evt_`
- `source=agent_confirmed`
- `createdBy=agent`
- `title` or `content` includes `[SMOKE TEST]`

## Commands

Default local safety check:

```bash
node scripts/smoke-agent-life-event-write.mjs
```

Online API health plus authenticated-flow skip when no token:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
  node scripts/smoke-agent-life-event-write.mjs
```

Future controlled real-write smoke:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="..." \
RUN_AGENT_WRITE_SMOKE=true \
EXPECT_AGENT_WRITE_ENABLED=true \
SMOKE_TEST_PREFIX="[SMOKE TEST]" \
  node scripts/smoke-agent-life-event-write.mjs
```

## Current Phase 5.3 Boundary

Phase 5.3 only adds smoke preparation.

It does not:

- Enable production real writes.
- Change Cloud Run env.
- Deploy API or Web.
- Modify Firestore Rules.
- Deploy Firestore Rules.
- Modify Firebase Auth.
- Modify frontend code.
- Change confirm/write business semantics.
- Execute real `create_life_event` writes.
- Implement reminder, memory, calendar, MCP, or additional action types.

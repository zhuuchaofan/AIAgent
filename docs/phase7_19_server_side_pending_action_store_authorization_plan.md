# Phase 7.19 Server-side PendingActionStore Authorization Plan

Date: 2026-07-09

## 1. Current Server-side Access Context

Phase 7.18 decided that the primary future access path for pending action,
memory, and execution state should be:

```text
Web Frontend
  -> Cloud Run API
  -> Guarded runtime
  -> Server-side store
  -> Firestore
```

Firestore Rules are still useful as defense-in-depth, but they are not the main
enforcement layer for Cloud Run server-side Firestore access through Google
Cloud server libraries, Admin SDK style credentials, or Application Default
Credentials.

Current code observations:

- frontend Firebase initialization uses Auth only
- frontend data actions call the API with bearer tokens
- `FirebaseAuthMiddleware` verifies Firebase ID tokens and places `userId` in
  `HttpContext.Items`
- `Program.cs` registers server-side `FirestoreDb`
- existing Firestore repositories and services are server-side
- the Phase 7.9 `IPendingActionStore` is not registered in production DI
- the existing `IPendingAgentActionStore` preview store is a separate older
  Agent Preview path under `agent_pending_actions`

## 2. PendingActionStore Trust Boundary

Future `IPendingActionStore` implementations must not trust these fields from
frontend request bodies:

- `userId`
- `userSubjectRef`
- `status`
- `guardDecision`
- `executionReady`
- `executed`
- `wroteData`
- `expiresAt`
- `createdAt`
- `updatedAt`
- tool name / tool id / tool version
- adapter id
- risk level
- input hash
- preview hash
- confirmation hash
- server-only payload refs
- payload body
- release gate approval state

These fields must be generated, bound, validated, or overwritten by the
server-side runtime.

The API may accept a user intent or confirmation decision, but it must construct
store requests from authenticated context and server-side validated state.

## 3. Required Server-side Authorization Invariants

Required invariants:

- every pending action is bound to an authenticated user id
- user id comes from verified auth context, never request body
- pending action id is scoped under the owning user
- read, confirm, cancel, expire, and execution readiness checks verify owner
- `confirmed` does not mean `executed`
- `execution_ready` does not mean `executed`
- denied guard decisions cannot become `execution_ready`
- missing Release Gate blocks real execution
- expired actions cannot confirm or execute
- cancelled actions cannot execute
- already executed actions cannot execute again
- confirm request cannot resubmit server-only payload
- client cannot overwrite server-only fields
- audit fields are server-written
- idempotency key hash / version / etag semantics need future design
- all blocked responses use sanitized reasons

Current skeleton note: `GuardedExecutionRequest` includes `UserSubjectRef`.
Future API endpoints must build that request server-side from auth context and
must not deserialize it directly from a client body.

## 4. API-level Checks

### Create preview

Required checks:

- authenticated user exists in `HttpContext.Items`
- user id is not accepted from request body
- tool registry resolves allowed tool id / version
- preview adapter produces sanitized preview
- server-only payload is stored only as a reference
- no write occurs

### Create pending action

Required checks:

- pending action owner is authenticated user
- server generates pending action id
- server sets status and timestamps
- server sets TTL
- server hashes input / preview / idempotency key
- server writes audit refs
- client-provided status, payload refs, executed flags, or release gate refs are ignored or rejected

### Get pending action

Required checks:

- owner lookup by authenticated user
- return sanitized projection only
- do not return server-only payload refs unless explicitly safe
- do not return raw hashes, raw prompt, full context, or executable payload

### Confirm pending action

Required checks:

- owner lookup by authenticated user
- action is active and not expired
- status transition is allowed
- confirmation request hash is server-computed
- server revalidates tool id / version / input hash / preview hash
- confirmation cannot include executable payload
- confirmation only records confirmation state; it does not execute

### Cancel pending action

Required checks:

- owner lookup by authenticated user
- terminal statuses cannot be reopened
- audit event is recorded
- cancellation reason is sanitized

### Evaluate guard

Required checks:

- request is built server-side
- guard loads pending action by authenticated owner
- release gate evaluator defaults closed
- guard-denied result records `execution_blocked` or equivalent
- no tool execution happens during guard evaluation

### Execution readiness check

Required checks:

- owner verified
- confirmation exists and matches
- hashes match
- idempotency passes
- expired / cancelled / blocked actions fail closed
- `execution_ready` remains future-gated

### Future execute action

Required checks:

- endpoint disabled by default
- Release Gate required
- service account / IAM reviewed
- idempotency / replay protection enforced
- server-only payload retrieved server-side
- audit event written before and after attempt
- failure does not silently mark executed

## 5. Store Method-level Checks

The current Phase 7.9 interface names differ from the future friendly names.
The rules below map to current methods.

| Current method | Required caller context | Allowed transition | Forbidden client fields | Audit / idempotency |
| --- | --- | --- | --- | --- |
| `CreateAsync` | authenticated user from API context | none -> `confirmation_required` or `preview_created` | `status`, `executed`, `wroteData`, server payload body, release gate refs | create audit ref, idempotency hash |
| `GetByIdAsync` | authenticated user | none | direct raw document response | access audit optional |
| `GetActiveByUserAsync` | authenticated user | lazy expire only | cross-user query | sanitized projection only |
| `UpdateStatusAsync` | server runtime only | expected status -> allowed new status | arbitrary status, client timestamps | status-change audit, etag/version future |
| `RecordConfirmationReferenceAsync` | confirmation runtime | active -> confirmed reference state | raw confirmation payload | confirmation hash, audit ref |
| `RecordGuardDecisionReferenceAsync` | guard runtime | confirmed -> `execution_ready` or `execution_blocked` | client guard decision | guard decision ref, audit ref |
| `MarkExpiredAsync` | store / runtime / cleanup | active -> `expired` | client expiry override | expiration audit |
| `CancelAsync` | authenticated owner through API | active -> `cancelled` | server-only fields | cancellation audit |
| `CheckIdempotencyKeyHashAsync` | server runtime | none | raw idempotency key | hash-only lookup |

Future methods such as `ConfirmAsync`, `MarkExecutionReadyAsync`,
`MarkExecutedAsync`, and `ExpireAsync` should preserve the same invariants.

`MarkExecutedAsync` must not be introduced until Release Gate approves a real
execution path.

## 6. Firestore Document Shape Impact

The Phase 7.12 candidate path remains recommended:

```text
users/{userId}/pendingActions/{pendingActionId}
```

Reasons:

- easier owner enforcement in server code
- simpler query scope
- safer audit inspection
- lower cross-user leakage risk
- aligns with existing user-scoped LifeOS data layout

Server-side access does not remove the value of user-scoped paths. It shifts
the enforcement responsibility from Firestore Rules to Cloud Run API and store
method invariants.

This phase does not create the collection.

## 7. IAM / Service Account Boundary

Future Cloud Run Firestore writes should use a reviewed service account.

Requirements:

- avoid broad Owner / Editor roles as the long-term plan
- use a dedicated Cloud Run service account
- review Firestore permissions before production write enablement
- review project binding and environment variables
- document rollback permissions
- keep write flags default-off until Release Gate

This phase does not modify IAM, service accounts, env vars, or Cloud Run.

## 8. Audit / Observability

Future audit fields:

- `createdByUserId`
- `confirmedByUserId`
- `cancelledByUserId`
- `expiredBy`
- `guardDecision`
- `guardReason`
- `releaseGateState`
- `createdAt`
- `confirmedAt`
- `cancelledAt`
- `executionReadyAt`
- `executedAt`
- `expiredAt`
- `requestId`
- `traceId`
- `idempotencyKeyHash`
- `toolId`
- `toolVersion`
- `schemaVersion`

Audit rules:

- store hashes and refs, not raw secrets
- do not record raw prompts
- do not record full context
- do not record complete provider requests
- do not record complete executable payloads
- do not record complete Firestore document bodies
- blocked reasons exposed to client must be sanitized

This phase does not migrate schema.

## 9. Future Test Strategy

Recommended test layering:

- unit tests for status transition matrix
- fake store authorization tests
- API auth tests using mock auth context
- confirmation runtime tests
- guard runtime tests
- offline guard chain tests
- idempotency / replay tests
- audit projection tests
- Firestore emulator tests only after explicit approval
- production smoke only after Release Gate

Near-term priority should be fake-store / API-level authorization tests before
Rules unit test dependency installation.

## 10. Impact on Previous Firestore Rules Phases

Phase 7.13 through Phase 7.17 remain valid, but their role is secondary:

- they prevent future frontend direct Firestore mistakes
- they document default-deny expectations
- they support a future client-direct Rules regression track if approved
- they are not the primary safety line for Cloud Run server-side writes

The main Phase 7 track should now prioritize server-side authorization and
store invariants.

## 11. Non-goals

Phase 7.19 does not:

- implement `FirestorePendingActionStore`
- connect production DI
- connect real Firestore
- create Firestore collections
- modify Firestore Rules
- modify `firebase.json`
- install dependencies
- deploy
- modify Cloud Run env
- write `users/{userId}/pendingActions`
- write `users/{userId}/memories`
- write `life_events`
- execute real tools
- call external provider APIs
- process secrets
- register a real executor
- push commits

## 12. Current Code Risk Notes

No stop-the-work issue was found in this phase.

Observed notes:

- frontend direct Firestore business writes were not found
- existing API auth middleware binds `userId` server-side
- existing Agent confirm endpoint reads `userId` from `HttpContext.Items`
- existing old `IPendingAgentActionStore` checks owner before get / confirm
- existing Agent life_event write branch is feature-gated
- the new Phase 7 `IPendingActionStore` is not wired into production DI
- future guard API must not trust client-provided `UserSubjectRef`
- future store implementation must avoid accepting client-provided status /
  guard / execution fields

## 13. Recommended Next Phase

Recommended next phase:

```text
Phase 7.20 PendingActionStore Transition Matrix / Fake Store Authorization Tests
```

Rationale:

- tests server-side invariants without real Firestore
- uses existing `IPendingActionStore` skeleton
- avoids production DI
- avoids deployment
- avoids real writes
- directly follows the Phase 7.18 access path decision

Alternative:

```text
Phase 7.20 PendingActionStore Server-side Authorization Test Plan
```

if another docs-only checkpoint is desired before adding tests.

Final conclusion: future PendingActionStore authorization must be enforced by
Cloud Run API and server-side store invariants. Firestore Rules remain
defense-in-depth, while the main safety path shifts to authenticated server
context, owner checks, status transition rules, guard and release gate
enforcement, audit, idempotency, and IAM review.

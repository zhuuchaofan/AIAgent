# Phase 7.18 Firestore Access Path Decision

Date: 2026-07-09

## 1. Current Architecture Assumption

LifeOS / LifeAgent is deployed with a Cloud Run API as the server-side runtime.
The preferred future write path for pending action, guarded execution, memory,
and tool runtime state is:

```text
Web Frontend
  -> Cloud Run API
  -> Guarded runtime
  -> Server-side store
  -> Firestore
```

Current code aligns with this direction:

- frontend Firebase initialization uses Auth only
- frontend business operations use server actions that call API endpoints
- API registers `FirestoreDb` server-side
- API verifies Firebase ID tokens before protected endpoint handling
- existing Firestore reads/writes are server-side services and repositories

No frontend direct Firestore business access was found in the current code scan.

## 2. Decision

Future pending action, memory, and execution state must not be written directly
from the frontend to Firestore.

All writes must go through the Cloud Run API. The Cloud Run backend owns:

- authentication
- user id binding
- request ownership checks
- confirmation verification
- guard decision
- release gate evaluation
- execution authorization
- store write validation
- idempotency
- audit logging
- sanitized response projection

The client may hold a Firebase Auth token and submit commands to the API, but it
must not hold execution authority, server-only payloads, direct pending action
write permission, or release gate authority.

## 3. Firestore Rules Repositioning

Firestore Rules are not the primary enforcement layer for the Cloud Run
server-side Firestore path when the backend uses Google Cloud server libraries,
Admin SDK style credentials, or Application Default Credentials.

Rules tests remain valuable, but their role is secondary:

- protect against future client-direct Firestore access
- prevent accidental frontend direct Firestore wiring
- document defense-in-depth expectations
- test possible future Firebase client SDK access paths
- keep default-deny behavior visible for pending action documents

Therefore, Phase 7.13 through Phase 7.17 are not invalidated. They become a
secondary / defense-in-depth track rather than the main safety track for the
future server-side Pending Action Store.

## 4. Primary Safety Boundary

After this decision, the primary safety boundary is server-side:

- Cloud Run auth boundary
- Firebase ID token verification in API middleware
- server-side user id binding
- service-level authorization
- `IPendingActionStore` method-level invariants
- guarded execution validation
- release gate checks
- service account IAM and least privilege
- audit logging with hashes / refs / sanitized reasons only
- idempotency and replay protection
- TTL and expiration handling
- no `confirmed == executed` shortcut
- no `execution_ready == executed` shortcut

Firestore Rules can still deny client access, but they must not be treated as
the main control for server-side writes.

## 5. Impact on Previous Phases

Phase 7.13 to Phase 7.17 remain useful:

- Phase 7.13 defines Rules / emulator test scope
- Phase 7.14 adds fake rules matrix fixture
- Phase 7.15 adds a test-only Rules draft
- Phase 7.16 adds non-running rules unit test skeleton
- Phase 7.17 reviews dependency enablement

Their positioning changes:

- secondary / defense-in-depth track
- useful for preventing future frontend direct Firestore mistakes
- useful if a future client-direct SDK path is explicitly approved
- not the primary blocker for Cloud Run server-side store design

The next main line should not be dependency installation for
`@firebase/rules-unit-testing` unless the user explicitly chooses to continue
the client-direct Rules regression track.

## 6. Recommended Next Phase

Recommended next phase:

```text
Phase 7.19 Server-side PendingActionStore Authorization Plan
```

Suggested focus:

- define server-side authorization invariants
- define user id binding from API middleware to store calls
- define store method preconditions
- define guard runtime and confirmation runtime call order
- define service account IAM assumptions
- define audit and idempotency requirements
- define fake / emulator-first implementation path
- keep production writes disabled until explicit approval

Do not jump directly to Firestore Rules dependency installation unless the user
explicitly approves a client-direct Rules regression track.

## 7. Release Gate

Before real Firestore Pending Action Store implementation:

- user approval
- service account review
- IAM least-privilege review
- Firestore collection path approval
- document schema review
- server-side authorization plan approved
- emulator / fake-first implementation approved
- rollback plan approved
- audit field review
- idempotency and replay strategy approved
- no production write until explicit approval

Before production enablement:

- Release Gate approval
- deployment plan
- Cloud Run env review
- production DI review
- rollback plan
- smoke / canary plan
- no raw secret / raw prompt / full context in trace or audit

## 8. Non-goals

Phase 7.18 does not:

- implement a real Pending Action Store
- implement `FirestorePendingActionStore`
- connect real Firestore
- create Firestore collections
- modify production `firestore.rules`
- modify `firebase.json`
- install npm dependencies
- start Firestore emulator
- deploy
- modify Cloud Run environment variables
- connect production DI
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- call external provider APIs
- process secrets
- push commits

Final conclusion: future pending action / memory / execution state should use
Cloud Run server-side API access to Firestore. Firestore Rules remain useful as
defense-in-depth for client-direct access prevention, but the main safety line
should shift to server-side authorization, store invariants, IAM, release gate,
audit, and idempotency.

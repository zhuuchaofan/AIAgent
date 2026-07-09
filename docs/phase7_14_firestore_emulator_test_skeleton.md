# Phase 7.14 Firestore Emulator Test Skeleton

Date: 2026-07-09

## 1. Phase 7.14 Goal

Phase 7.14 establishes a low-risk Firestore emulator / Rules test skeleton plan
for the future Pending Action Store. It adds a fake-only Rules test matrix
fixture, but it does not add executable emulator infrastructure.

Goals:

- prepare Firestore emulator / Rules tests for Pending Action Store
- record whether the current repo already has emulator test infrastructure
- define future rules test structure, fixture shape, and command plan
- keep no-real-Firestore, no-production-Rules-change, and no-deploy boundaries
- prepare for Phase 7.15 Firestore Store Implementation with Emulator

## 2. Non-goals

Phase 7.14 does not:

- implement `FirestorePendingActionStore`
- connect real Firestore
- create real collections
- modify production `firestore.rules`
- modify `firebase.json`
- modify Cloud Run environment variables
- deploy
- connect production DI
- enable durable memory write
- write `users/{userId}/memories`
- write `life_events`
- execute real tool actions
- call external provider APIs
- process real secrets
- push commits

## 3. Current Repository Assessment

Actual repository state checked in this phase:

- `firebase.json` exists
- `firebase.json` points to `firestore.rules`
- `firestore.rules` exists
- no dedicated emulator configuration is present in `firebase.json`
- no Rules test directory was found
- no Firestore emulator CI entry was found
- no root package for Rules tests was found
- `life-agent-web/package.json` exists for the Next.js app
- `life-agent-web/package.json` does not include `@firebase/rules-unit-testing`
- no npm script was found for Firestore emulator tests
- no Makefile or shell script was found for Rules tests

Because only basic Firebase config exists, this phase uses docs + fixture +
skeleton plan. It intentionally avoids adding a half-wired dependency stack.

## 4. Proposed Test Structure

Recommended future structure:

```text
docs/fixtures/phase7_14/
  pending_action_rules_test_matrix.json

tests/firestore-rules/
  package.json
  pending-action.rules.test.ts
  firestore.rules.test-only

scripts/
  test-firestore-rules.sh
```

Current Phase 7.14 adds only:

```text
docs/fixtures/phase7_14/pending_action_rules_test_matrix.json
```

Future executable tests should live outside `life-agent-web` unless there is a
deliberate decision to make Firebase Rules testing part of the web package.
Keeping Rules tests isolated avoids mixing frontend dependencies with emulator
test dependencies.

## 5. Test-only Rules Draft Boundary

Phase 7.14 does not modify `firestore.rules`.

Any future draft Rules file must be clearly named test-only, for example:

```text
tests/firestore-rules/firestore.pending-actions.test-only.rules
```

Rules boundary:

- draft Rules are not production Rules
- draft Rules must not be referenced by production `firebase.json`
- draft Rules must not replace `firestore.rules`
- future production Rules changes require a separate Release Gate
- sanitized projection should be returned by server API, not direct client
  Firestore reads

Pseudo policy for future tests:

```text
match /users/{userId}/pendingActions/{pendingActionId} {
  allow read, write: if false;
}

match /users/{userId}/pendingActionPayloads/{payloadId} {
  allow read, write: if false;
}

match /users/{userId}/pendingActionAuditRefs/{auditRefId} {
  allow read, write: if false;
}
```

## 6. Rules Test Fixture

Added fake-only fixture:

```text
docs/fixtures/phase7_14/pending_action_rules_test_matrix.json
```

The fixture covers:

1. unauthenticated client cannot read pending action
2. authenticated same user cannot read full server document directly
3. authenticated same user cannot write pending action directly
4. authenticated same user cannot update status directly
5. authenticated cross-user cannot read pending action
6. authenticated cross-user cannot write pending action
7. client cannot read server-only payload reference
8. client cannot read audit-only refs if projection exists
9. client cannot create document with prohibited fields
10. client cannot modify `status`
11. client cannot modify `expiresAt`
12. client cannot modify `serverOnlyPayloadRef`
13. client cannot inject `releaseGateDecisionRef`
14. client cannot mark `executed`
15. server/admin path remains responsible for writes, future only

Fixture safety rules:

- fake user ids only
- fake hashes only
- no real user profile data
- no real tokens
- no real secrets
- no real provider requests
- no real knowledge context
- no real Firestore document body
- no real API keys

## 7. Emulator Command Plan

No runnable command is added in Phase 7.14.

Future command candidates:

```text
firebase emulators:exec --only firestore "npm test"
```

```text
npm --prefix tests/firestore-rules test
```

```text
scripts/test-firestore-rules.sh
```

Future prerequisites:

- choose Rules test directory
- approve adding `@firebase/rules-unit-testing`
- decide whether to use TypeScript or JavaScript for Rules tests
- add test-only Rules file or generate Rules fixture
- configure emulator host/port locally
- ensure tests use fake fixture data only
- ensure no real GCP, real Firestore, real secrets, or deployment are needed

## 8. Security Assertions

The Phase 7.14 fixture and future tests must assert:

- default deny
- unauthenticated denied
- cross-user denied
- direct client full document read denied
- direct client write denied
- direct client status update denied
- client cannot set `executed`
- client cannot set `releaseGateDecisionRef`
- client cannot access server-only payload
- no raw secret
- no raw prompt
- no full context
- no complete provider request
- no complete executable payload
- no complete Firestore document body in audit-like fixture
- `confirmed` does not mean `executed`
- `execution_ready` does not mean `executed`

## 9. Phase 7.15 Readiness

Before entering Phase 7.15:

- Phase 7.14 skeleton is reviewed
- Rules matrix is approved
- emulator test structure is approved
- adding `@firebase/rules-unit-testing` is approved
- adding test-only draft Rules is approved
- Firebase emulator CI usage is approved
- no production Rules change occurs unless Release Gate approves it
- no real Firestore or production data is used

Recommended Phase 7.15 scope:

- add isolated `tests/firestore-rules` test harness
- add `@firebase/rules-unit-testing`
- add test-only Rules file
- load fake matrix fixture
- run local emulator-only Rules tests
- still avoid production DI, deployment, Cloud Run env, real Firestore, and
  production Rules changes

Final conclusion: Phase 7.14 adds a fake-only Rules test matrix and skeleton
plan. It does not modify production `firestore.rules`, modify `firebase.json`,
connect real Firestore, create collections, deploy, change env, connect
production DI, write data, execute tools, call providers, process secrets, or
push.

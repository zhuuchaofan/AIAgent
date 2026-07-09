# Phase 7.16 Firestore Rules Unit Test Skeleton

Date: 2026-07-09

## 1. Phase 7.16 Goal

Phase 7.16 creates a non-running Firestore Rules unit test skeleton for the
future Pending Action Store. It documents the dependency decision, test-only
Rules boundary, case mapping, and future command plan.

This phase does not create a runnable emulator test package because the repo
does not yet include `@firebase/rules-unit-testing`, a Rules test package, or an
approved dependency installation path.

## 2. Non-goals

Phase 7.16 does not:

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
- install dependencies
- push commits

## 3. Dependency / Package Manager Assessment

Actual repo assessment:

- root has no `package.json`
- `life-agent-web/package.json` exists
- `life-agent-web/package-lock.json` exists
- npm is the package manager style currently present
- `life-agent-web/package.json` does not include `@firebase/rules-unit-testing`
- no `tests/firestore-rules/package.json` exists
- no Rules unit test npm script exists
- no Firebase emulator CI entry was found
- `firebase.json` exists and points to production `firestore.rules`
- no emulator config is present in `firebase.json`

Decision:

- do not install `@firebase/rules-unit-testing`
- do not modify `life-agent-web/package.json`
- do not create a misleading runnable test package without dependencies
- add a non-running skeleton README under `tests/firestore-rules/`
- keep the test-only Rules draft under docs fixtures

## 4. Rules Unit Test Skeleton

Added:

```text
tests/firestore-rules/README.md
```

This file is a skeleton only. It records:

- future fixture paths
- future test-only Rules path
- fake project id
- all required Phase 7.14 matrix cases
- future command shape
- emulator-only boundary

It intentionally does not add:

- `package.json`
- `package-lock.json`
- JS/TS test file
- npm script
- Firebase emulator config
- CI config

## 5. Test-only Rules Boundary

Future tests should use:

```text
docs/fixtures/phase7_15/pending_action_test_only.rules
```

Boundary:

- file name contains `test_only`
- not referenced by `firebase.json`
- does not replace production `firestore.rules`
- only for future emulator tests
- default deny
- client cannot directly read full pending action documents
- client cannot directly write pending action documents
- client cannot write `status`, `executed`, or `releaseGateDecisionRef`
- client cannot read server-only payloads or audit refs
- server/admin path remains future-only
- sanitized projection remains API-owned, not direct Firestore read

## 6. Rules Case Mapping

Future tests should load:

```text
docs/fixtures/phase7_14/pending_action_rules_test_matrix.json
```

and map cases as follows:

| Matrix case | Unit test name |
| --- | --- |
| unauthenticated_read_denied | unauthenticated client cannot read pending action |
| same_user_full_read_denied | same user cannot read full server document directly |
| same_user_direct_create_denied | same user cannot write pending action directly |
| same_user_status_update_denied | same user cannot update status directly |
| cross_user_read_denied | cross-user cannot read pending action |
| cross_user_write_denied | cross-user cannot write pending action |
| server_only_payload_read_denied | client cannot read server-only payload reference |
| audit_refs_read_denied | client cannot read audit-only refs |
| prohibited_fields_create_denied | client cannot create document with prohibited fields |
| status_modify_denied | client cannot modify `status` |
| expires_at_modify_denied | client cannot modify `expiresAt` |
| server_only_payload_ref_modify_denied | client cannot modify `serverOnlyPayloadRef` |
| release_gate_decision_inject_denied | client cannot inject `releaseGateDecisionRef` |
| mark_executed_denied | client cannot mark `executed` |
| server_admin_path_future_only | server/admin path remains responsible for writes, future only |

All cases must use fake subjects, fake ids, fake refs, and fake project id
`demo-life-agent-phase7`.

## 7. Command Plan

No runnable command is added in this phase.

Future command candidate:

```text
firebase emulators:exec --only firestore "npm --prefix tests/firestore-rules test"
```

Future prerequisites:

- user approves adding `@firebase/rules-unit-testing`
- user approves creating `tests/firestore-rules/package.json`
- package lock generation is approved
- Firebase CLI availability is confirmed
- Java runtime availability is confirmed
- tests are configured to use emulator only
- fake project id is used
- no real GCP, real Firestore, real secret, or production data is required

## 8. Why No Production Changes

Production `firestore.rules` is unchanged because Phase 7.16 is not a Rules
release. Production Rules changes require Release Gate approval, review,
deployment plan, rollback plan, and explicit user confirmation.

`firebase.json` is unchanged because pointing it at a draft file could create a
production footgun. Future test-only wiring must be isolated from production
configuration.

`FirestorePendingActionStore` is not implemented because this phase is only the
Rules unit test skeleton. Store implementation must remain emulator-only in a
future approved phase and must not connect production DI.

Real Firestore is not connected because this phase has no need for GCP, secrets,
production data, or deployed resources.

## 9. Phase 7.17 Readiness

Before Phase 7.17:

- Rules unit test skeleton reviewed
- `@firebase/rules-unit-testing` decision approved
- test package lock changes approved
- emulator test script approved
- test-only Rules draft reviewed
- production `firestore.rules` still not approved for modification
- Firestore Store implementation remains emulator-only
- no production DI wiring
- no Cloud Run env change
- no deployment

Recommended Phase 7.17 scope:

- add isolated `tests/firestore-rules/package.json`
- add approved devDependency
- add executable emulator-only unit tests
- keep fake fixture data only
- do not modify production `firestore.rules`
- do not modify `firebase.json` for production

Final conclusion: Phase 7.16 adds documentation and a non-running rules unit
test skeleton. It does not install dependencies, add runnable emulator tests,
modify production Rules, modify Firebase config, connect real Firestore, create
collections, deploy, change env, connect production DI, write data, execute
tools, call providers, process secrets, or push.

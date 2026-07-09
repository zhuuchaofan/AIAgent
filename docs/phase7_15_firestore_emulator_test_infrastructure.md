# Phase 7.15 Firestore Emulator Test Infrastructure

Date: 2026-07-09

## 1. Phase 7.15 Goal

Phase 7.15 prepares Firestore emulator / Rules test infrastructure for the
future Pending Action Store. This phase adds a test-only Rules draft and a
docs-first infrastructure plan. It does not add executable emulator tests.

Goals:

- prepare a safe Rules test infrastructure path
- align with the Phase 7.14 rules matrix fixture
- document whether the current repo is ready for runnable Rules tests
- explain why `FirestorePendingActionStore` is still not implemented
- explain why production `firestore.rules` is still unchanged
- prepare for Phase 7.16 Firestore Store Implementation with Emulator

## 2. Non-goals

Phase 7.15 does not:

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

## 3. Current Infra Assessment

Actual repository assessment:

- `firebase.json` exists
- `firebase.json` points to production `firestore.rules`
- `firestore.rules` exists
- no Firestore emulator config is present in `firebase.json`
- no dedicated Rules test directory exists
- no Firestore emulator CI config was found
- no root `package.json` exists for test infrastructure
- `life-agent-web/package.json` exists and uses npm lockfile style
- `life-agent-web/package-lock.json` exists
- no `pnpm-lock.yaml` or `yarn.lock` was found
- `@firebase/rules-unit-testing` is not present
- no npm script exists for Firestore Rules tests
- no Makefile or Rules test shell script exists

Decision:

- Do not add `@firebase/rules-unit-testing` in this phase because it would
  require a new dependency install and a test harness decision.
- Do not modify `life-agent-web/package.json`; Rules tests should likely live
  outside the frontend package.
- Do add a test-only Rules draft under docs fixtures.
- Do keep runnable emulator infrastructure for a future approved phase.

## 4. Test-only Rules Draft

Added:

```text
docs/fixtures/phase7_15/pending_action_test_only.rules
```

Boundary:

- file name includes `test_only`
- it is not referenced by `firebase.json`
- it does not replace `firestore.rules`
- it is not production Rules
- it only expresses Phase 7.13 / 7.14 default-deny policy
- future production Rules changes require a separate Release Gate

Draft policy:

- client cannot directly read full pending action documents
- client cannot directly write pending action documents
- client cannot update `status`
- client cannot set `executed`
- client cannot inject `releaseGateDecisionRef`
- client cannot access server-only payload documents
- client cannot access audit-only refs
- server/admin path remains future-only
- sanitized projection should be returned by API, not direct Firestore read

## 5. Rules Test Skeleton

No runnable JS/TS test skeleton is added in this phase.

Reason:

- no existing Rules test package exists
- no Rules test dependency exists
- adding dependencies would require a package ownership decision
- adding a half-runnable test file would create ambiguity about whether it is
  expected to pass locally or in CI

Future skeleton candidate:

```text
tests/firestore-rules/
  package.json
  package-lock.json
  firestore.pending-actions.test-only.rules
  pending-action.rules.test.ts
  fixtures/pending_action_rules_test_matrix.json
```

Future package should use fake data only and must not require real GCP, real
Firestore, real secrets, production Rules, production DI, or deployment.

## 6. Rules Test Case Mapping

The Phase 7.14 matrix maps to future emulator tests as follows:

| Matrix case | Future emulator assertion |
| --- | --- |
| unauthenticated_read_denied | unauthenticated client cannot read pending action |
| same_user_full_read_denied | authenticated same user cannot read full server document directly |
| same_user_direct_create_denied | authenticated same user cannot write pending action directly |
| same_user_status_update_denied | authenticated same user cannot update status directly |
| cross_user_read_denied | authenticated cross-user cannot read pending action |
| cross_user_write_denied | authenticated cross-user cannot write pending action |
| server_only_payload_read_denied | client cannot read server-only payload reference |
| audit_refs_read_denied | client cannot read audit-only refs if projection exists |
| prohibited_fields_create_denied | client cannot create document with prohibited fields |
| status_modify_denied | client cannot modify `status` |
| expires_at_modify_denied | client cannot modify `expiresAt` |
| server_only_payload_ref_modify_denied | client cannot modify `serverOnlyPayloadRef` |
| release_gate_decision_inject_denied | client cannot inject `releaseGateDecisionRef` |
| mark_executed_denied | client cannot mark `executed` |
| server_admin_path_future_only | server/admin SDK remains responsible for writes, future only |

The future tests should load:

```text
docs/fixtures/phase7_14/pending_action_rules_test_matrix.json
```

and apply:

```text
docs/fixtures/phase7_15/pending_action_test_only.rules
```

inside a local emulator only.

## 7. Command / Script Plan

No command is added in this phase.

Future command candidates:

```text
firebase emulators:exec --only firestore "npm --prefix tests/firestore-rules test"
```

```text
npm run test:firestore-rules
```

```text
scripts/test-firestore-rules.sh
```

Future prerequisites:

- Firebase CLI available locally or in CI
- Java runtime available for the Firestore emulator
- approved `tests/firestore-rules` package location
- approved `@firebase/rules-unit-testing` devDependency
- approved test-only Rules file
- fake-only fixtures
- no real GCP project requirement
- no real Firestore requirement
- no real secret requirement

This command path can run in CI after dependency and emulator setup are
approved. It must remain emulator-only.

## 8. Safety Assertions

The test-only draft and future skeleton must preserve:

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
- no complete Firestore document body
- `confirmed` does not mean `executed`
- `execution_ready` does not mean `executed`

## 9. Phase 7.16 Readiness

Before Phase 7.16:

- test-only Rules draft reviewed
- Phase 7.14 rules matrix approved
- emulator infrastructure approach approved
- dependency decision approved
- script / command plan approved
- production `firestore.rules` change still not approved
- Firestore Store implementation remains emulator-only
- no production DI wiring
- no Cloud Run env change
- no deployment

Recommended Phase 7.16 scope:

- create isolated `tests/firestore-rules` package
- add approved devDependency
- add runnable emulator-only tests
- copy or reference fake matrix fixture
- use only test-only Rules
- avoid production `firestore.rules` changes
- avoid real Firestore and production writes

Final conclusion: Phase 7.15 adds docs and a test-only Rules draft. It does not
add dependencies, runnable emulator tests, production Rules changes,
`firebase.json` changes, real Firestore access, production DI, deployment,
Cloud Run env changes, data writes, tool execution, provider calls, secret
handling, or push.

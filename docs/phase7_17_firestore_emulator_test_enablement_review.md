# Phase 7.17 Firestore Emulator Test Enablement Review

Date: 2026-07-09

## 1. Goal

Phase 7.17 reviews how to enable future Firestore emulator Rules unit tests for
the Pending Action Store. It does not implement those tests.

The review answers:

- whether to add an independent test package
- whether to introduce `@firebase/rules-unit-testing`
- whether to introduce `firebase-tools`
- dependency and lockfile impact
- future test execution structure
- approval and Release Gate prerequisites

## 2. Current Repository Assessment

Observed repository state:

- `firebase.json` exists and points to production `firestore.rules`
- production `firestore.rules` exists
- `docs/fixtures/phase7_14/pending_action_rules_test_matrix.json` exists
- `docs/fixtures/phase7_15/pending_action_test_only.rules` exists
- `tests/firestore-rules/README.md` exists as a non-running skeleton
- root has no `package.json`
- `life-agent-web/package.json` exists
- `life-agent-web/package-lock.json` exists
- package manager style present in repo is npm
- no `pnpm-lock.yaml` or `yarn.lock` was found
- no `@firebase/rules-unit-testing` dependency exists
- no `firebase-tools` dependency exists
- no Firestore emulator test package exists
- no Firestore emulator test script exists
- no CI entry for Firestore emulator Rules tests was found

Conclusion: the repo is ready for an enablement plan, but not yet ready for a
runnable Rules unit test without adding a test package and dependencies.

## 3. Independent Test Package Decision

Recommendation: add an independent test package in a future approved phase.

Recommended location:

```text
tests/firestore-rules/
```

Rationale:

- avoids mixing emulator-only dependencies into `life-agent-web`
- keeps frontend build/deploy dependency surface unchanged
- lets Rules tests own their package lock and scripts
- makes CI ownership clear
- keeps production API and production DI untouched

Future layout:

```text
tests/firestore-rules/
  package.json
  package-lock.json
  README.md
  fixtures/pending_action_rules_test_matrix.json
  rules/pending_action_test_only.rules
  pendingActionRules.test.js
```

The fixture may either be copied from docs or read from:

```text
docs/fixtures/phase7_14/pending_action_rules_test_matrix.json
```

The test-only Rules file may either be copied from docs or read from:

```text
docs/fixtures/phase7_15/pending_action_test_only.rules
```

## 4. Dependency Assessment

### `@firebase/rules-unit-testing`

Status: not installed.

Version candidate:

```text
@firebase/rules-unit-testing@latest
```

Reason:

- provides Firestore Rules test environment helpers
- supports authenticated / unauthenticated test contexts
- supports emulator-only read/write assertions
- maps directly to the Phase 7.14 matrix cases

Lockfile impact:

- requires a new `tests/firestore-rules/package-lock.json`, if using an
  isolated package
- should not modify `life-agent-web/package-lock.json`
- should not introduce production runtime dependency

Approval required:

- yes, because adding the dependency requires an install and lockfile change

### `firebase-tools`

Status: not installed as repo dependency.

Version candidate:

```text
firebase-tools@latest
```

Reason:

- provides `firebase emulators:exec`
- can orchestrate Firestore emulator lifecycle for local and CI tests

Lockfile impact:

- if installed locally in `tests/firestore-rules`, it will add a potentially
  large devDependency tree to that package lockfile
- alternatively, CI/local developers can use an externally installed Firebase
  CLI without committing `firebase-tools`

Recommendation:

- prefer not adding `firebase-tools` as a package dependency until CI strategy
  is approved
- document Firebase CLI as a prerequisite first
- revisit local devDependency only if reproducibility requires it

Approval required:

- yes, either to add it as devDependency or to mandate it in CI

## 5. Future Test Execution Structure

Future fake project id:

```text
demo-life-agent-phase7
```

Future command candidates:

```text
firebase emulators:exec --only firestore "npm --prefix tests/firestore-rules test"
```

```text
npm --prefix tests/firestore-rules test
```

The first command owns emulator lifecycle. The second command assumes the
Firestore emulator is already running and the test environment points to it.

Future package scripts:

```json
{
  "scripts": {
    "test": "node pendingActionRules.test.js",
    "test:emulator": "firebase emulators:exec --only firestore \"npm test\""
  }
}
```

These scripts are future candidates only and are not added in Phase 7.17.

## 6. Rules Case Coverage

Future unit tests should cover every Phase 7.14 matrix case:

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

All tests must use fake users, fake ids, fake refs, and fake project id only.

## 7. Production Configuration Boundary

Phase 7.17 does not change:

- production `firestore.rules`
- `firebase.json`
- Cloud Run env
- production Firebase config
- production DI
- runtime code

Future test-only wiring must not point `firebase.json` at draft Rules unless a
separate test-only config is created and clearly isolated from production.

Production `firestore.rules` changes require Release Gate approval.

## 8. Release Gate / Approval Checklist

Before enabling runnable Rules unit tests:

- approve `tests/firestore-rules/` as independent package
- approve npm as package manager for that package
- approve adding `@firebase/rules-unit-testing`
- decide whether `firebase-tools` is a devDependency or external prerequisite
- approve lockfile creation
- approve test-only Rules file location
- approve fixture copy vs docs reference strategy
- approve fake project id
- approve local command and CI command
- confirm no production `firestore.rules` change
- confirm no `firebase.json` production pointer change
- confirm no real Firestore / real GCP / real secrets

Before any production Rules change:

- Release Gate approval
- production Rules review
- rollback plan
- deployment plan
- emulator tests passing
- offline Phase 7 tests passing

## 9. Phase 7.18 Recommendation

Recommended next phase:

```text
Phase 7.18 Firestore Rules Unit Test Package Plan
```

Suggested scope:

- add `tests/firestore-rules/package.json`
- add dependency plan as explicit approval item
- optionally add runnable test only after dependency install approval
- keep all tests emulator-only
- keep production `firestore.rules` and `firebase.json` unchanged

Do not proceed to `FirestorePendingActionStore` implementation until executable
Rules tests and emulator-only boundaries are reviewed.

Final conclusion: Phase 7.17 is an enablement review only. It does not install
dependencies, run Firebase emulator, modify production Rules, modify Firebase
config, connect real Firestore, create collections, implement
`FirestorePendingActionStore`, connect production DI, deploy, write data, call
providers, process secrets, or push.

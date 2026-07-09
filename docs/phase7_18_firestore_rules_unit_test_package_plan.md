# Phase 7.18 Firestore Rules Unit Test Package Plan

Date: 2026-07-09

## 1. Goal

Phase 7.18 designs the package plan for future Firestore Rules unit tests. It
does not create a runnable package, install dependencies, start the emulator,
or modify production Firebase configuration.

This phase answers:

- whether to add an independent `tests/firestore-rules` package
- what the minimum future `package.json` should look like
- which dependencies are needed
- how lockfiles should be generated
- how to avoid polluting `life-agent-web`
- what approvals are required before enablement

## 2. Current Package Landscape

Observed package state:

- root has no `package.json`
- `life-agent-web/package.json` exists
- `life-agent-web/package-lock.json` exists
- npm is the package manager style currently in use
- `life-agent-web` is a Next.js app package, not a test infrastructure package
- `tests/firestore-rules/README.md` exists as a non-running skeleton
- `tests/firestore-rules/package.json` does not exist
- `tests/firestore-rules/package-lock.json` does not exist
- `@firebase/rules-unit-testing` is not installed
- `firebase-tools` is not installed as a repo dependency
- no Firestore emulator npm script exists
- no CI entry for Firestore Rules tests was found

Recommendation: do not add Rules test dependencies to `life-agent-web`.

Reason:

- avoids changing frontend build/deploy dependency surface
- avoids mixing emulator-only tooling with production web package dependencies
- allows Rules tests to own their scripts and lockfile
- makes future rollback straightforward

## 3. Recommended Package Boundary

Recommended future package:

```text
tests/firestore-rules/
```

Recommended future files:

```text
tests/firestore-rules/
  package.json
  package-lock.json
  README.md
  pendingActionRules.test.js
  fixtures/pending_action_rules_test_matrix.json
  rules/pending_action_test_only.rules
```

Phase 7.18 does not create these package files. It only documents the proposed
shape.

## 4. Proposed `package.json`

Proposed future content:

```json
{
  "name": "life-agent-firestore-rules-tests",
  "private": true,
  "type": "module",
  "scripts": {
    "test": "node pendingActionRules.test.js",
    "test:emulator": "firebase emulators:exec --only firestore \"npm test\""
  },
  "devDependencies": {
    "@firebase/rules-unit-testing": "latest"
  }
}
```

Status:

- not installed
- not executable yet
- requires approval
- does not belong in `life-agent-web/package.json`
- should generate its own package lock only after approval

Optional `firebase-tools` variant:

```json
{
  "devDependencies": {
    "@firebase/rules-unit-testing": "latest",
    "firebase-tools": "latest"
  }
}
```

This variant should be used only if the team wants hermetic local/CI emulator
commands from the package itself.

## 5. Dependency Decision

### `@firebase/rules-unit-testing`

Recommendation: required for runnable Rules unit tests.

Reason:

- creates isolated Rules test environments
- supports authenticated and unauthenticated test contexts
- can assert allow/deny behavior against the Firestore emulator
- maps directly to the Phase 7.14 rules matrix

Install status:

- not installed in Phase 7.18

Future install impact:

- adds `@firebase/rules-unit-testing` to
  `tests/firestore-rules/package.json`
- generates `tests/firestore-rules/package-lock.json`
- should not modify `life-agent-web/package.json`
- should not modify `life-agent-web/package-lock.json`

Approval required: yes.

### `firebase-tools`

Recommendation: start as local/CI prerequisite, not package dependency.

Reason:

- required for `firebase emulators:exec`
- can be provided by developer machine or CI image
- installing it as devDependency may add a large dependency tree

Future options:

1. External prerequisite:
   - no package dependency
   - command uses installed Firebase CLI
   - smaller lockfile

2. DevDependency:
   - more reproducible package script
   - larger lockfile
   - requires explicit approval

Approval required: yes, either way.

## 6. Future Command Shape

Future commands:

```text
npm --prefix tests/firestore-rules test
```

```text
firebase emulators:exec --only firestore "npm --prefix tests/firestore-rules test"
```

If `firebase-tools` is installed inside the package, a future command may be:

```text
npm --prefix tests/firestore-rules run test:emulator
```

Phase 7.18 does not run these commands.

## 7. Lockfile Impact

Future approved install should create:

```text
tests/firestore-rules/package-lock.json
```

Expected boundaries:

- no changes to `life-agent-web/package-lock.json`
- no changes to root package lock because no root package exists
- no `node_modules` committed
- no production runtime dependencies added
- package lock belongs only to the test package

If lockfile generation touches unrelated package files, the install should be
stopped and reviewed.

## 8. Test Fixture and Rules Source

Recommended fixture source:

```text
docs/fixtures/phase7_14/pending_action_rules_test_matrix.json
```

Recommended test-only Rules source:

```text
docs/fixtures/phase7_15/pending_action_test_only.rules
```

Future tests may copy these into `tests/firestore-rules/fixtures` and
`tests/firestore-rules/rules` for package locality. If copied, docs must remain
the design source and test package copies must be clearly test-only.

Production `firestore.rules` must not be used as the initial test target for
Pending Action Store because the production Rules are not yet approved for this
new collection shape.

## 9. Release Gate Checklist

Before dependency install:

- approve creating `tests/firestore-rules/package.json`
- approve npm as the package manager
- approve installing `@firebase/rules-unit-testing`
- decide whether `firebase-tools` is external prerequisite or devDependency
- approve generating `tests/firestore-rules/package-lock.json`
- approve future emulator command
- approve use of test-only Rules draft
- confirm production `firestore.rules` is not referenced
- confirm `firebase.json` is not modified
- confirm no real Firestore / real GCP / real secrets

Before production Rules work:

- executable emulator tests pass
- Phase 7 offline tests remain green
- production Rules change reviewed
- rollback plan approved
- deployment plan approved
- user gives explicit Release Gate approval

## 10. Rollback Plan

If future package enablement fails, remove:

```text
tests/firestore-rules/package.json
tests/firestore-rules/package-lock.json
tests/firestore-rules/node_modules
tests/firestore-rules/pendingActionRules.test.js
tests/firestore-rules/fixtures/
tests/firestore-rules/rules/
```

Rollback must preserve:

- production `firestore.rules`
- `firebase.json`
- `life-agent-web/package.json`
- `life-agent-web/package-lock.json`
- Cloud Run env
- production Firebase config
- production DI

No cleanup should touch production Firestore or production data.

## 11. Phase 7.19 Recommendation

Recommended next phase:

```text
Phase 7.19 Firestore Rules Test Package Approval Gate
```

Purpose:

- ask for explicit approval to create the package
- decide dependency versions
- decide whether to install `firebase-tools`
- decide whether to generate package lock
- decide whether to add first runnable emulator-only test file

Do not implement `FirestorePendingActionStore` until the Rules test package
decision is approved and emulator-only test execution is available.

Final conclusion: Phase 7.18 is docs-only. It does not create a real package,
install dependencies, generate a lockfile, run tests, start Firebase emulator,
modify production Rules, modify Firebase config, touch `life-agent-web`
packages, connect real Firestore, create collections, implement
`FirestorePendingActionStore`, connect production DI, deploy, call providers,
process secrets, or push.

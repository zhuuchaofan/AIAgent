# Phase 7.16 Firestore Rules Unit Test Skeleton

This directory is a non-running skeleton for future Firestore emulator Rules
unit tests. It intentionally has no `package.json`, no dependency lockfile, and
no executable test script in Phase 7.16.

## Boundary

- test-only skeleton
- no production `firestore.rules` changes
- no `firebase.json` changes
- no real Firestore
- no real GCP project
- no real secrets
- no production data
- no production DI

Future tests should use:

- fixture matrix:
  `docs/fixtures/phase7_14/pending_action_rules_test_matrix.json`
- test-only draft rules:
  `docs/fixtures/phase7_15/pending_action_test_only.rules`
- fake project id:
  `demo-life-agent-phase7`

## Future Test Cases

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

## Future Command Shape

Do not run this until a future phase approves the dependency and emulator
setup:

```text
firebase emulators:exec --only firestore "npm --prefix tests/firestore-rules test"
```

Future setup must add `@firebase/rules-unit-testing` as a test-only dependency
in an isolated package and must remain emulator-only.

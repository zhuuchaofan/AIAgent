# Phase 7.13 Firestore Emulator / Rules Test Plan

Date: 2026-07-09

## 1. Phase 7.13 Goal

Phase 7.13 is a docs-only test plan for future Firestore emulator and Rules
tests around the Pending Action Store. It does not add emulator wiring or Rules
tests yet.

The plan verifies how a future implementation can test:

- Phase 7.12 recommended collection shape
- client cannot directly read or write full pending action documents
- server-only payload references are not client-readable
- user-scoped ownership and cross-user blocking
- TTL, `expiresAt`, and cleanup behavior
- query and index shapes
- prohibited raw fields are absent
- no production write, no deployment, and no real Firestore access

## 2. Non-goals

Phase 7.13 does not:

- implement `FirestorePendingActionStore`
- connect real Firestore
- create real collections
- modify production Firestore Rules
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

If future work requires real GCP resources, real Firestore, secrets,
deployment, Cloud Run env changes, MCP, Rules changes, or production writes,
it must stop for explicit approval.

## 3. Current Repo Baseline

Current repo observations:

- `firebase.json` exists and points to `firestore.rules`
- `firestore.rules` exists and protects current LifeOS collections
- no dedicated Firestore emulator test structure was found
- no Rules unit test harness was found
- Phase 7.9 `IPendingActionStore` is not production DI wiring
- Phase 7.10 / 7.11 guard runtime tests remain offline and fake-store based

Given this baseline, Phase 7.13 should remain docs-only. Emulator setup and
Rules tests should be introduced in a separate low-risk phase.

## 4. Emulator Scope

Future Firestore emulator tests should cover:

- pending action document create through server-owned store code
- read by same user through server-owned store code
- cross-user read blocked
- direct client write blocked
- direct client status update blocked
- client cannot read server-only fields
- client can only access sanitized projection through API, future only
- expired action query behavior
- cleanup candidate query behavior
- audit refs retained as references
- prohibited raw fields absent

Important boundaries:

- emulator tests are not production validation
- emulator tests do not mean Rules are deployed
- emulator tests do not create real GCP resources
- emulator setup itself should be a separate low-risk phase

## 5. Rules Test Matrix

Future Rules tests should cover:

| Case | Actor | Operation | Expected |
| --- | --- | --- | --- |
| unauthenticated_read | unauthenticated client | read pending action | deny |
| same_user_full_read | authenticated owner | read full pending action document directly | deny |
| same_user_create | authenticated owner | create pending action directly | deny |
| same_user_status_update | authenticated owner | update `status` directly | deny |
| cross_user_read | authenticated other user | read pending action | deny |
| cross_user_write | authenticated other user | write pending action | deny |
| payload_ref_read | authenticated owner | read server-only payload target | deny |
| audit_ref_read | authenticated owner | read audit-only refs directly | deny |
| prohibited_fields_create | authenticated owner | create doc with raw/prohibited fields | deny |
| status_modify | authenticated owner | modify `status` | deny |
| expires_modify | authenticated owner | modify `expiresAt` | deny |
| payload_ref_modify | authenticated owner | modify `serverOnlyPayloadRef` | deny |
| release_gate_inject | authenticated owner | inject `releaseGateDecisionRef` | deny |
| mark_executed | authenticated owner | set `executed=true` | deny |
| server_admin_write | server/admin SDK | write pending action | future implementation only |

This matrix is a design artifact only. Phase 7.13 does not modify
`firestore.rules`.

## 6. Candidate Rules Draft

Future policy bullets:

- default deny
- clients cannot list `pendingActions`
- clients cannot read full pending action documents
- clients cannot write pending action documents
- clients cannot update `status`
- clients cannot set `executed`
- clients cannot set release gate decisions
- clients cannot access server-only payload documents or objects
- all real writes go through server API / Admin SDK
- sanitized projections are returned by API, not direct Firestore reads
- cross-user access must block
- future Rules changes require Release Gate approval

Pseudo policy:

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

This is not applied to `firestore.rules`.

## 7. Emulator Data Fixtures

Future emulator fixtures should be fake-only and include:

- `userId`
- `userIdHash`
- `otherUserId`
- `otherUserIdHash`
- `pendingActionId`
- `previewId`
- `confirmationId`
- `status`
- `riskLevel`
- `expiresAt`
- `sanitizedPreviewRef`
- `serverOnlyPayloadRef`
- `auditEventRefs`
- `prohibitedFieldsMarker`
- `attemptedClientOperation`
- `expectedAllow`
- `expectedDenyReason`

Fixture rules:

- use fake data only
- no real user profile data
- no real tokens
- no real secrets
- no real provider requests
- no real knowledge context
- no raw prompt
- no complete executable payload
- no complete Firestore document body

## 8. Query / Index Test Plan

Future query tests:

- by user + status + expiresAt
- by pendingActionId
- by previewId
- by confirmationId
- by traceId
- by idempotencyKeyHash
- by toolId / toolVersion
- cleanup by status + expiresAt

Emulator-testable:

- server store can query active actions for one user
- server store can find by preview id
- server store can find by confirmation id
- server store can enforce idempotency hash within user scope
- expired records are excluded from active queries

Rules-testable:

- client direct list is denied
- client cross-user query is denied
- client cannot query server-only payloads
- client cannot query audit refs

Composite index candidates:

- `userSubjectRef`, `status`, `expiresAt`
- `userSubjectRef`, `idempotencyKeyHash`
- `userSubjectRef`, `previewId`
- `userSubjectRef`, `confirmationId`
- `status`, `expiresAt`
- `toolId`, `toolVersion`, `createdAt`

Indexes requiring confirmation before production:

- cleanup queries by `status` + `expiresAt`
- audit/debug queries by `traceId`
- operational queries by `toolId` / `toolVersion`

Queries forbidden to clients:

- full pending action document reads
- server-only payload lookups
- audit ref lookups
- idempotency hash queries
- release gate decision queries

## 9. TTL / Cleanup Test Plan

Future TTL and cleanup tests should verify:

- `expiresAt` in the past is treated as expired
- active action queries filter expired records
- guard runtime blocks expired records
- cleanup job processes only expired / cancelled / blocked records
- cleanup does not delete audit references
- cleanup may delete or invalidate server-only payload refs
- cleanup records sanitized audit refs only
- Firestore TTL policy remains a future option

Phase 7.13 does not implement cleanup jobs. Firestore TTL policy and cleanup
jobs are production-affecting write paths and require a separate Release Gate.

## 10. Security Assertions

Future emulator and Rules tests should assert:

- no raw secret
- no raw prompt
- no full context
- no complete provider request
- no complete executable payload
- no complete Firestore document body in audit
- client cannot access server-only fields
- client cannot set `status`
- client cannot set `executed`
- client cannot set release gate decision refs
- cross-user access is denied
- missing auth is denied
- default deny is preserved
- all writes are server-side only
- `confirmed` is not treated as `executed`
- `execution_ready` is not treated as `executed`

## 11. Future Implementation Phases

Recommended future phases:

| Phase | Scope | Risk | Approval |
| --- | --- | --- | --- |
| Phase 7.14 Firestore Emulator Test Skeleton | add emulator/rules test harness with fake fixtures only | low to medium | recommended before execution |
| Phase 7.15 Firestore Store Implementation with Emulator | implement store against emulator only, no production DI | medium | required |
| Phase 7.16 Production Wiring Plan | docs-only production DI/env/rollback plan | low, unless env is changed | required before implementation |
| Phase 8 Release Gate / Online Canary | real Firestore, Rules, env, deploy, canary | high | explicit approval required |

Final conclusion: Phase 7.13 defines the emulator and Rules test plan for a
future Pending Action Store. It does not connect real Firestore, create
collections, modify production Rules, modify env, deploy, connect production DI,
write memories or life events, execute tools, call providers, handle secrets,
or push.

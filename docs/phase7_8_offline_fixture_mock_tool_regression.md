# Phase 7.8 Offline Fixture / Mock Tool Regression

Date: 2026-07-09

## 1. Background

Phase 7.3 through Phase 7.7 defined the contracts for runtime trace / audit,
preview tool adapters, confirmation runtime, pending action storage, and guarded
execution. Phase 7.8 adds offline fixture regression coverage for those
contracts without enabling runtime execution.

This phase uses local fake data only. It does not call external providers, does
not use real Firestore, does not write production data, does not deploy, does
not modify Cloud Run environment variables, does not modify Firestore Rules,
and does not enable MCP.

## 2. Phase 7.8 Goals

Phase 7.8 verifies that:

- preview tool adapter contract can be represented by fixtures
- sanitized preview does not leak sensitive values
- pending action keeps server-only payload as a reference
- confirmation requests require server-side revalidation
- guarded execution can block unsafe actions or mark future readiness
- `confirmed` does not mean `executed`
- `execution_ready` does not mean `executed`
- trace and audit contain only hashes, references, and sanitized summaries
- missing Release Gate defaults to blocked execution
- external calls and write intent default to blocked

## 3. Non-goals

Phase 7.8 does not:

- execute real tool actions
- call external providers
- generate real provider pilot reports
- connect a real Firestore write path
- write real memories
- write `users/{userId}/memories`
- write `life_events`
- modify deployment configuration
- modify Firestore Rules
- enable MCP
- process real secrets
- push commits

## 4. Offline Fixture Schema

Fixture file:

```text
LifeAgent.Tests/Fixtures/Phase7_8/offline_mock_tool_regression.json
```

Top-level fields:

- `schemaVersion`
- `fixtureSetId`
- `title`
- `cases`

Each case includes:

- `fixtureId`
- `title`
- `description`
- `toolId`
- `toolVersion`
- `adapterId`
- `inputPayload`
- `sanitizedPreviewExpected`
- `serverOnlyPayloadExpectedShape`
- `pendingActionExpected`
- `confirmationRequest`
- `confirmationExpectedResult`
- `guardDecisionExpected`
- `traceAuditExpectedEvents`
- `blockedReasonExpected`
- `prohibitedFieldsExpectedAbsent`
- `riskLevel`
- `writeIntent`
- `externalCall`
- `releaseGateState`
- `idempotencyKeyBehavior`
- `ttlExpirationBehavior`

Fixtures must use fake data only. They must not include real customer data,
real tokens, real provider requests, real Firestore document bodies, or real
knowledge context.

## 5. Mock Tool Case Matrix

The Phase 7.8 fixture covers:

1. low-risk read-only preview allowed
2. write-intent memory create preview blocked because Release Gate is missing
3. expired pending action confirmation rejected
4. cross-user confirmation blocked
5. stale tool version blocked
6. input hash mismatch blocked
7. preview hash mismatch blocked
8. duplicate confirmation idempotent
9. external call requested but blocked
10. high-risk action requires Release Gate
11. secret-like value redacted from preview and audit
12. raw prompt / full context prohibited from trace
13. cancelled action cannot execute
14. confirmed action still not executed
15. `execution_ready` remains future-gated

## 6. Regression Assertions

Offline tests assert:

- client-visible response does not contain server-only payload
- trace / audit do not contain secret, token, raw prompt, or full context fields
- preview does not claim execution
- confirmed does not claim execution
- execution readiness does not claim execution
- missing Release Gate blocks execution
- external call blocks by default
- write intent blocks by default when no Release Gate allows it
- expired action cannot confirm
- cross-user replay cannot confirm
- duplicate confirm returns the same idempotent result
- blocked reason is sanitized
- audit reason may be structured but cannot leak raw content

## 7. Trace / Audit Checks

Trace and audit fixture events must contain only:

- ids
- hashes
- references
- sanitized summaries
- policy decisions
- guard decisions
- Release Gate decisions
- redaction results
- blocked reason categories

Trace and audit must not contain:

- raw secrets
- raw prompts
- full context
- complete provider requests
- complete executable payloads
- complete Firestore document bodies
- raw auth claims
- raw idempotency keys

## 8. Guard / Release Gate Checks

Offline fixture expectations:

- no Release Gate means execution is blocked
- `writeIntent=true` blocks unless a future gate allows it
- `externalCall=true` blocks unless a future gate allows it
- high risk blocks or requires Release Gate
- stale policy blocks
- stale tool version blocks
- cross-user replay blocks
- expired action is rejected / expired
- cancelled action cannot execute
- confirmed remains not executed
- execution-ready remains not executed

## 9. Implementation Notes

This phase adds:

- one design / regression plan document
- one fake JSON fixture
- one xUnit offline fixture regression test file

The tests are deterministic and local. They parse fixture JSON and validate the
contract invariants. They do not instantiate production runtime services, do not
read environment secrets, do not access Firestore, do not use network, and do
not call external providers.

## 10. Future Stage Relationship

Recommended later path:

- Phase 7.9 Store Interface Skeleton: only after user approval, add local
  interfaces or models without creating collections or changing Rules.
- Phase 7.10 Guard Runtime Skeleton: only after user approval, add a local guard
  evaluator skeleton that remains no-write and no-external-call.
- Phase 8 Release Gate / Online Canary: future planning only. Online canaries,
  production writes, env changes, Firestore Rules changes, provider pilots,
  MCP, and external calls require explicit user approval.

Phase 7.8 stops at offline mock regression. It does not start runtime
implementation or online validation.

## 11. Verification Plan

Run:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj --filter Phase78`
- `git diff --stat`
- `git diff --check`
- `git status --short`
- `git log --oneline -4`

Do not run real smoke tests, provider pilots, external API calls, tests requiring
real secrets, tests requiring real Firestore, deploy commands, or gcloud write
commands.

## 12. Closeout Criteria

Phase 7.8 is complete when:

- offline fixture schema is documented
- mock tool case matrix exists
- local fixture exists with fake data only
- offline tests validate core no-leak / no-execution invariants
- verification passes
- a local commit is created
- no push is performed

Final conclusion: Phase 7.8 adds offline mock regression coverage for Phase 7
contracts. It does not execute tools, write data, call providers, deploy, modify
Firestore, or enable real execution.

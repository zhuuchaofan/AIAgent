# Phase 6.7A Post-implementation Local Verification Plan

Date: 2026-07-01

## Scope

This plan defines local pre-deployment verification for Phase 6.7A Read-only
Memory Retrieval Minimal Integration.

This plan does not:

- Deploy.
- Change Cloud Run environment variables.
- Change Firestore Rules.
- Connect real Firestore memory runtime.
- Enable durable memory writes.
- Enter Phase 6.8 implementation.
- Push commits.

## Current State

- Phase 6.7A implementation is complete and committed.
- Implementation commit: `db191900d661caba011a518553ac44cf6fe4ae60`
- Read-only `MemoryContextProvider` is wired.
- Default flags are off.
- With no env configured, runtime uses no-op behavior.
- With flags off, response payload contract remains unchanged.
- With flags on, runtime uses fake/in-memory read-only provider only.
- Real Firestore memory repository is not connected.
- No production API endpoint was added.
- No `users/{userId}/memories` write path was added.
- No `life_events` write path was added.
- Retrieval does not create pending actions.
- Retrieval does not trigger extraction.
- Retrieval does not automatically create `save_memory_preview`.
- Durable memory write remains disabled.
- No deployment has been performed.
- No push has been performed.

## Verification Goals

Local verification must prove:

- Default-off behavior is unchanged.
- With no env configured, responses do not include `memoryContext`.
- With flags off, retrieval is not called.
- With flags on, retrieval remains read-only.
- Retrieval enabled does not create pending actions.
- Retrieval enabled does not write `users/{userId}/memories`.
- Retrieval enabled does not write `life_events`.
- Retrieval failure fallback is safe.
- RAG behavior has no regression.
- `life_event` preview-only behavior has no regression.
- `save_memory_preview` behavior has no regression.

## Local Test Matrix

### A. No env / default-off

Expected:

- `NoopMemoryContextProvider`.
- No `memoryContext` diagnostics in response payload.
- Response contract unchanged.
- `wroteData=false`.
- `previewOnly` semantics unchanged.
- No retrieval call.

### B. `ENABLE_MEMORY_RETRIEVAL=false`

Expected:

- Same behavior as default-off.
- No `memoryContext` diagnostics in response payload.
- No retrieval call.
- No write.

### C. `ENABLE_MEMORY_RETRIEVAL=true` and `ENABLE_MEMORY_CONTEXT_IN_AGENT=false`

Expected:

- Runtime context remains disabled.
- No `memoryContext` diagnostics in response payload.
- Retrieval is not called.
- No write.

### D. Both flags true

Expected:

- Fake/in-memory read-only provider is used.
- Safe diagnostics may be emitted.
- Diagnostics do not expose memory content.
- No data is written.
- No pending action is created by retrieval.
- No extraction is triggered.

### E. Provider failure

Expected:

- Agent run does not fail.
- Runtime falls back to no-memory behavior.
- If flags are enabled, safe failure diagnostics may be emitted.
- No data is written.

## Required Commands

Required local commands:

```bash
dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj
```

```bash
git diff --check
```

```bash
git status --short
```

Optional local flag-focused verification may use test-only configuration or
unit-test fixtures. It must not require deployment.

## Smoke Readiness Criteria

Preview-only API deployment smoke may be considered only after:

- `dotnet test` passes.
- `git status` is clean.
- Default-off contract is verified.
- No-write behavior is verified.
- Cloud Run write flags remain unchanged.
- Firestore Rules remain unchanged.
- MCP remains unchanged.
- The user explicitly approves deployment.

## No-write Checklist

| Check | Required result |
| --- | --- |
| `users/{userId}/memories` write | must remain false |
| `life_events` write from retrieval | must remain false |
| Pending action from retrieval | must remain false |
| Extraction trigger | must remain false |
| `save_memory_preview` auto creation | must remain false |
| Durable memory write | must remain false |
| Real Firestore memory repository | must remain absent |

## Stop Conditions

Stop verification or implementation follow-up if any of these occur:

- Default-off response contract changes.
- `memoryContext` appears when flags are off.
- Retrieval creates a pending action.
- Retrieval writes memory.
- Retrieval touches `life_events`.
- Retrieval triggers extraction.
- RAG regression occurs.
- `life_event` regression occurs.
- `save_memory_preview` regression occurs.
- Any Cloud Run, Firestore Rules, or MCP change is required.

## Final Conclusion

Phase 6.7A local verification plan is ready.

This does not deploy.

This does not enable real memory write.

Preview-only deployment smoke requires separate user approval.

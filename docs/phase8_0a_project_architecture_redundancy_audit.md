# Phase 8.0A Project Architecture / Redundancy Audit

Date: 2026-07-09

## 1. Executive Summary

The project is healthy enough to continue Phase 8 user-visible MVP hardening,
but it now has three parallel pending-action lines and one legacy real-write
path that must be explicitly isolated before deployment or cleanup.

This audit is documentation-only. No code, config, deployment, package, rules,
or production data changes were made.

## 2. Current Architecture Map

Current real user-visible path:

```text
Next.js web
  -> Firebase Auth token
  -> server actions in life-agent-web/src/app/actions/
  -> Cloud Run API / .NET Minimal API
  -> FirebaseAuthMiddleware sets HttpContext.Items["userId"]
  -> endpoint/service layer
  -> Firestore-backed services for existing life/RAG/legacy agent preview data
```

Current Agent / Pending Action paths:

```text
Path A: Existing Agent Preview
  AgentPreview.tsx
  -> runAgentPreview()
  -> POST /api/agent/run
  -> AgentRunner / AgentActionExecutor
  -> IPendingAgentActionStore
  -> FirestorePendingAgentActionStore in production DI
  -> users/{userId}/agent_pending_actions/{actionId}

Path B: Existing Agent Confirm
  AgentPreview.tsx
  -> confirmAgentAction()
  -> POST /api/agent/confirm
  -> IPendingAgentActionStore
  -> preview confirm/cancel by default
  -> optional create_life_event write branch if both real-write flags are enabled

Path C: Phase 8.0 fake-first Pending Action MVP
  AgentPreview.tsx
  -> create/confirm/cancel Phase 8 server actions
  -> /api/agent/pending-actions/demo
  -> static Phase80PendingActionRuntime
  -> in-memory only
  -> no Firestore, no executor, no production DI

Path D: Phase 7 future runtime tooling
  IPendingActionStore
  -> GuardedExecutionRuntime
  -> offline tests / fixtures
  -> not wired to production DI
```

## 3. Current User-visible Capability

Visible capabilities today:

- authenticated life event ingestion, timeline, reminders, daily summary
- knowledge base management and RAG chat
- optional Agent Preview, gated by `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW`
- legacy Agent Preview can propose actions and confirm/cancel them
- Phase 8.0 Agent Preview panel can generate a fake pending action, confirm it,
  cancel it, and show `confirmed but not executed`

The user can see `pending`, `confirmed`, and `cancelled` in Phase 8.0. The UI
also shows `executed: false`, `wroteData: false`, and
`deny_all_no_real_execution`.

## 4. Redundancy Findings

| Area | Files | Finding | Risk | Recommendation |
| --- | --- | --- | --- | --- |
| Pending action model split | `LifeAgent.Api/Models/Agent/PendingAgentAction.cs`, `LifeAgent.Api/Services/Agent/PendingActions/PendingActionRecord.cs`, `LifeAgent.Api/Services/Agent/Phase8/Phase80PendingActionRuntime.cs` | Three pending action shapes exist: legacy persisted Agent Preview, future Phase 7 contract, and Phase 8 demo view/record. | Medium | Keep short-term, but create a Phase 8.1 consolidation plan that names one canonical future domain model. |
| Store interface split | `IPendingAgentActionStore.cs`, `IPendingActionStore.cs`, `InMemoryPendingAgentActionStore.cs`, `FirestorePendingAgentActionStore.cs` | Legacy store is wired to production DI; new store interface is only skeleton/offline; Phase 8 demo uses neither interface. | Medium | Keep legacy for existing preview, keep Phase 7 interface for future, but do not add a fourth store abstraction. |
| API duplication | `AgentEndpoints.cs` | `/api/agent/run` + `/api/agent/confirm` coexist with `/api/agent/pending-actions/demo`. | Medium | Mark demo endpoints as Phase 8 fake-first and decide before deployment whether they remain exposed. |
| UI duplication | `AgentPreview.tsx` | One component contains legacy Agent Preview run/confirm UI and Phase 8 demo pending action UI. | Medium | In Phase 8.1, split UI sections or copy so users understand legacy preview vs fake-first pending action. |
| Real-write path still present | `AgentEndpoints.cs`, `AgentLifeEventConfirmationWriteCoordinator.cs`, `AgentWriteFeatureOptions.cs` | `/api/agent/confirm` can enter real `life_event` write branch if `ENABLE_AGENT_WRITE_TOOLS` and `ENABLE_CREATE_LIFE_EVENT_TOOL` are both true. | High | Before deployment, explicitly verify flags are unset/false and document that Phase 8 demo does not use this path. |
| Production DI uses legacy Firestore pending store | `Program.cs`, `FirestorePendingAgentActionStore.cs` | Existing Agent Preview writes pending actions to `users/{userId}/agent_pending_actions`; Phase 8 demo is in-memory only. | Medium | Avoid claiming Phase 8 pending action persistence until new store is approved and wired. |
| Guard runtime not integrated with Phase 8 demo | `GuardedExecutionRuntime.cs`, `Phase80PendingActionRuntime.cs` | Phase 8 demo returns `deny_all_no_real_execution` but does not call GuardedExecutionRuntime. | Low | Accept for MVP; Phase 8.1 can add a read-only guard status projection without execution. |
| Firestore Rules track scope | `docs/phase7_13*` through `docs/phase7_17*`, `docs/phase7_18_firestore_access_path_decision.md` | Rules tests are planning/skeleton and secondary; not a mainline runtime blocker. | Low | Keep docs, but do not install rules test dependencies unless explicitly resuming that track. |
| Docs volume and phase overlap | `docs/phase4`, `docs/phase5`, `docs/phase6_*`, `docs/phase7_*`, `docs/phase8_*` | Many docs describe preview-only, canary, and real-write plans from older phases; some language predates Phase 7/8 decisions. | Medium | Add an index/status doc instead of deleting; label old docs as historical when referenced. |
| Test naming by phase | `LifeAgent.Tests/Phase7*`, `Phase80*`, `AgentSkeletonTest.cs` | Current and historical behavior tests are mixed; skeleton tests are valid but not all are production-wired. | Low | Keep tests; add grouping comments or a test map before refactoring. |

## 5. Old vs New Pending Action Comparison

| Track | Primary files | Current purpose | Storage | Production wiring | Recommendation |
| --- | --- | --- | --- | --- | --- |
| Legacy `PendingAgentAction` | `PendingAgentAction.cs`, `IPendingAgentActionStore.cs`, `FirestorePendingAgentActionStore.cs`, `InMemoryPendingAgentActionStore.cs` | Existing Agent Preview confirmation lifecycle, including persisted preview action records. | Firestore path `users/{userId}/agent_pending_actions/{actionId}` in production DI; in-memory in tests. | Wired in `Program.cs` as `FirestorePendingAgentActionStore`. | Keep for now; isolate from Phase 8 demo and audit real-write flag behavior before deployment. |
| Future `PendingAction` | `Services/Agent/PendingActions/*`, `GuardedExecutionRuntime.cs` | Phase 7 canonical future contract for server-side pending action, guard, audit refs, hashes, idempotency, execution readiness. | No production implementation yet. | Not wired to production DI. | Keep as future target; do not wire to Firestore until Release Gate approves schema, IAM, DI, and migration. |
| Phase 8.0 demo pending action | `Phase80PendingActionRuntime.cs`, `AgentEndpoints.cs`, `AgentPreview.tsx` | User-visible fake-first confirmation loop. | Static in-memory process state only. | Endpoint static runtime; no Firestore DI. | Keep for MVP hardening; either replace with future store or remove before real production pending-action persistence. |

The main ambiguity is naming: users and developers may see all three as
"pending action." The code responsibilities are currently separable, but not
self-evident enough for a deployment handoff.

## 6. Store / Repository Consolidation Recommendation

Recommended order:

1. Keep `IPendingAgentActionStore` unchanged while the old Agent Preview path
   still exists.
2. Keep `IPendingActionStore` as the intended future server-side runtime
   contract.
3. Keep `Phase80PendingActionRuntime` as temporary MVP scaffolding only.
4. Before implementing Firestore `IPendingActionStore`, decide whether legacy
   `agent_pending_actions` data is historical-only or must migrate.
5. Do not connect production DI for the new store until:
   - schema is approved
   - path is approved
   - owner checks are implemented
   - audit/idempotency behavior is tested
   - Release Gate approves Firestore writes

Do not delete any store yet. The safer near-term cleanup is naming and
documentation: label legacy, future, and demo tracks clearly.

## 7. Guard / Execution Gate Status

Current guard facts:

- `GuardedExecutionRuntime` consumes `IPendingActionStore`, not the legacy
  `IPendingAgentActionStore`.
- It defaults to `DenyAllReleaseGateEvaluator`.
- It can mark `execution_ready` only when release gate evaluation allows
  execution; default deny blocks that.
- Its responses preserve `Executed = false`, `WroteData = false`, and
  `ExternalCallMade = false`.
- Phase 8.0 demo does not call this runtime; it hard-codes
  `deny_all_no_real_execution` in the demo response.

Known risk:

- Existing `/api/agent/confirm` is not the Phase 7 guarded runtime. It is the
  legacy confirmation path and contains a separate real-write branch for
  `create_life_event` behind feature flags.

Deployment blocker:

- Confirm that `ENABLE_AGENT_WRITE_TOOLS` and `ENABLE_CREATE_LIFE_EVENT_TOOL`
  are unset/false before any deployment that exposes Agent Preview.

## 8. Firestore / Rules Track Status

Current Firestore usage:

- backend registers `FirestoreDb` in `Program.cs`
- existing life events, reminders, daily summaries, documents, chat sessions,
  vectors, and legacy agent pending actions use server-side Firestore services
- frontend Firebase initialization is Auth-only; no direct frontend Firestore
  business path was identified in this audit
- legacy pending action storage writes
  `users/{userId}/agent_pending_actions/{actionId}`
- future Phase 7 path recommends `users/{userId}/pendingActions/{pendingActionId}`
  but does not create that collection
- Phase 8.0 demo does not write Firestore

Rules track:

- Phase 7.13-7.17 Firestore Rules / emulator docs are secondary /
  defense-in-depth
- no rules test dependencies are installed
- no emulator commands were run in this audit
- rules tests should not block Phase 8 fake-first server-side MVP hardening

## 9. Feature Flags / Env Flags

| Flag | Current use | Risk |
| --- | --- | --- |
| `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` | Web gate for mounting `AgentPreview`. | If enabled, both legacy Agent Preview and Phase 8 demo UI are exposed together. |
| `ENABLE_AGENT_WRITE_TOOLS` | Backend real-write gate. | Must remain false/unset unless Release Gate approves. |
| `ENABLE_CREATE_LIFE_EVENT_TOOL` | Backend create life event write gate. | Must remain false/unset unless Release Gate approves. |
| `USE_MOCK_AUTH` | Local auth bypass for tests/dev with mock users. | Must not be enabled unintentionally in production. |
| `USE_MOCK_LLM` | Local/mock LLM selection. | Useful for local tests; production behavior should be explicit. |
| `RUN_AGENT_WRITE_SMOKE`, `EXPECT_AGENT_WRITE_ENABLED` | Smoke script/runbook flags in docs. | Not runtime gates, but can confuse deployment instructions if not clearly labeled. |

Feature flag recommendation:

- Do not add more flags yet.
- Before deployment, explicitly decide whether Phase 8 demo endpoints are
  acceptable under the existing Agent Preview flag or need a separate backend
  guard.

## 10. Frontend / UX Consolidation Recommendation

Current UI structure:

- `page.tsx` mounts `AgentPreview` only when
  `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW === "true"`.
- `AgentPreview.tsx` contains:
  - legacy prompt/send Agent Preview flow
  - legacy proposed action confirm/cancel card
  - Phase 8.0 fake-first pending action card

UX risk:

- Users may not understand that the prompt-based legacy proposed action and
  the Phase 8 demo pending action are separate systems.
- The header still labels Agent Preview as `只读实验入口`, while the component
  also shows a confirmation flow. The copy is mostly safe but overloaded.

Recommendation:

- In Phase 8.1, keep one visible entry but split sections explicitly:
  - "Agent Preview: existing RAG/tool preview"
  - "Pending Action Demo: fake-first confirmation"
- Keep copy that says confirmed is not executed.
- Do not add another top-level navigation entry until the backend path is
  consolidated.

## 11. Tests Structure

Current mainline tests:

- `Phase80PendingActionRuntimeMvpTest.cs`: Phase 8 fake-first MVP behavior
- `Phase79PendingActionStoreSkeletonTest.cs`: future `IPendingActionStore`
  skeleton behavior
- `Phase710GuardRuntimeSkeletonTest.cs`: guard decision unit coverage
- `Phase711OfflineGuardChainTest.cs`: fixture-based guard chain coverage
- `Phase78OfflineMockRegressionTest.cs`: offline fixture regression
- `AgentSkeletonTest.cs`, `AgentLifeEvent*Test.cs`: legacy Agent Preview,
  confirmation, feature gate, and life event write path coverage

Future / non-running skeletons:

- Firestore Rules unit test skeleton and matrix under `docs/fixtures` and
  Phase 7.13-7.17 docs
- `tests/firestore-rules/README.md` / package plan if present locally

Test recommendation:

- Keep tests as-is for now.
- Add a short test map before deleting or merging anything.
- Treat Phase 8 MVP tests as the current user-visible fake-first regression.
- Treat Phase 7 guard/store tests as future runtime contract tests, not proof
  that production Agent Preview is already using the new guard runtime.

## 12. Docs Status

Positive status:

- `docs/phase7_18_firestore_access_path_decision.md` clearly moves primary
  enforcement to Cloud Run server-side access.
- `docs/phase7_19_server_side_pending_action_store_authorization_plan.md`
  defines owner/auth/store invariants.
- `docs/phase7_runtime_tooling_closeout.md` closes Phase 7.
- `docs/phase8_0_server_side_pending_action_runtime_mvp.md` states fake-first,
  in-memory, no Firestore, no execution.

Risk:

- Phase 4 and Phase 5 docs include older real-write planning and canary
  language. They are valuable history, but easy to misread as current approval.
- Phase 6 has multiple `6.7a` / `6.8a` plans/results that can look repetitive.
- Phase 7 has many docs-only subphases; Phase 7 closeout now prevents further
  splitting, but readers need an index.

Recommendation:

- Next cleanup should add a concise docs status index instead of deleting old
  docs.
- Mark older real-write docs as historical unless they are explicitly part of a
  future Release Gate.

## 13. Hidden Risk Paths

| Risk path | Why it matters | Current mitigation |
| --- | --- | --- |
| `/api/agent/confirm` real-write branch | Can write `life_event` if both backend flags are true. | Defaults false/unset; tests cover feature gate behavior. |
| Legacy `FirestorePendingAgentActionStore` | Agent Preview still writes pending action preview records to Firestore. | Owner-scoped path and auth middleware, but not Phase 8 fake-first. |
| Static in-memory Phase 8 runtime | State disappears on restart and is not shared across instances. | Acceptable for MVP; should not be represented as durable. |
| Demo endpoints exposed under Agent API | May ship if Agent Preview flag is enabled and backend deployed. | Need deployment gate decision. |
| `USE_MOCK_AUTH=true` | Would bypass real Firebase verification. | Intended local mode only; production env review required. |
| Docs with old canary instructions | Could be misused as current approval. | Release Gate language exists, but needs docs status index. |

## 14. Deployment Readiness Blockers

Before deploying Phase 8 work to Cloud Run, confirm:

1. Whether Phase 8 demo endpoints should be exposed in production.
2. `NEXT_PUBLIC_ENABLE_AGENT_PREVIEW` intended value for web deployment.
3. `ENABLE_AGENT_WRITE_TOOLS` is false/unset.
4. `ENABLE_CREATE_LIFE_EVENT_TOOL` is false/unset.
5. `USE_MOCK_AUTH` is not enabled.
6. No new Firestore `pendingActions` collection is expected.
7. Existing legacy `agent_pending_actions` Firestore writes are acceptable if
   Agent Preview is enabled.
8. UI copy clearly says fake/in-memory/not executed.
9. Logs do not expose raw secrets, raw prompts beyond existing accepted scope,
   or server-only payloads.
10. Rollback plan is documented.

This audit does not approve deployment.

## 15. Recommended Next Phase

Recommended next phase:

```text
Phase 8.1 User-visible Pending Action MVP Hardening
```

Scope should be small:

- clarify UI copy and section naming inside Agent Preview
- add a small backend/API test for Phase 8 demo endpoint auth/owner behavior if
  needed
- optionally add a lightweight docs status index for current vs historical docs
- do not wire Firestore
- do not add production DI
- do not add real execution
- do not deploy without a separate deployment gate

Alternative next step:

```text
Deployment Gate: Preview-only Phase 8 Demo Exposure Review
```

Choose this only if the immediate goal is to deploy the demo. It must include
Cloud Run env review and explicit approval.

Avoid another long docs-only phase unless it directly unlocks cleanup or
deployment.

## 16. Do-not-change List For This Audit

This phase did not modify:

- runtime/API code
- frontend code
- tests
- `firestore.rules`
- `firebase.json`
- Cloud Run env
- production config
- package files / lockfiles
- Firestore data
- collections
- secrets
- deployment state

Commands were read-only except for creating this audit document.

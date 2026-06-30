# Phase 6 Readiness Gate

Date: 2026-06-30

## Scope

This gate checks whether LifeOS / LifeAgent is ready to enter Phase 6 Memory Engine.

This is a readiness audit only. It does not implement Memory Engine, add memory writes, create Firestore collections, change Firestore Rules, change Cloud Run environment variables, deploy, enable real writes, add MCP servers, or change frontend behavior.

## 1. Agent Execution Contract Engine v1

Status: mostly ready for Phase 6 design.

- `AgentRunner` is now a pipeline coordinator only:
  `Resolve -> BuildContract -> BuildPlan -> Execute -> Validate -> Finalize`
- `AgentIntentResolver` owns intent detection, confidence, structured fallback intent, and read-only tool plan construction.
- `AgentContractValidator` owns intent-to-action contract validation, proposed action validation, `requiresConfirmation` enforcement, and life event payload validation.
- `AgentActionExecutor` owns contract execution:
  - proposed action creation for confirmation intents
  - read-only tool execution for RAG/document intents
  - structured fallback execution for unknown intents
- `AgentResponseFinalizer` owns `AgentRunResponse` construction and response shape consistency.
- Fallback is structured as an intent resolver output, not a direct ad hoc return in `AgentRunner`.
- `AgentRunResponse` has a unified contract shape:
  - `runId`
  - `mode`
  - `answer`
  - `proposedAction`
  - `actionType`
  - `requiresConfirmation`
  - `previewOnly`
  - `payload`
  - `wroteData`
  - `createdResourceId`
  - `toolCalls`

Known limitation:

- This is Execution Contract Engine v1, not a full pluggable workflow engine. It is enough for Phase 6 design, but Phase 6 implementation should avoid adding memory-specific branches back into `AgentRunner`.

## 2. Intent Coverage

| Intent | Trigger examples | actionType | requiresConfirmation | previewOnly | payload schema | fallback behavior |
| --- | --- | --- | --- | --- | --- | --- |
| `life_event` | `life_event`, `create_life_event`, `生活记录`, `生活事件` | `create_life_event` | true | true | life event preview payload validated by `LifeEventActionPayloadMapper` and `LifeEventPayloadValidator` | contract error if action or payload is invalid |
| `memory` | `帮我记一下`, `记一下`, `保存记忆`, `save memory`, `memory` | `save_memory_preview` | true | true | `{ originalMessage, previewOnly }` | contract error if confirmation contract is invalid |
| `reminder` | `提醒我`, `提醒`, `reminder` | top-level `reminder_action`, proposed action `create_reminder_preview` | true | true | `{ originalMessage, previewOnly }` | contract error if confirmation contract is invalid |
| `rag` | `根据文档回答`, `查一下文档`, `基于文档回答`, `answer with rag` | `preview_readonly_rag` | false | true | `{ intent, toolPlan }` | read-only structured response or tool failure response |
| `document` | `列出文档`, `文档状态`, `文档列表`, `show documents` | `document_action` | false | true | `{ intent, toolPlan }` | read-only structured response or tool failure response |
| `unknown` | no matched pattern, empty message | `preview_readonly_rag` | false | true | `{ intent, fallback, reason }` | explicit structured fallback |

Test coverage currently includes:

- intent matrix coverage for `life_event`, `memory`, `reminder`, `rag`, `document`, and `unknown`
- life event preview-only contract
- memory intent does not trigger life event action
- invalid proposed action type returns contract error
- proposed action without confirmation returns contract error

## 3. Confirm / Write Safety

Status: preview-only safe by default.

- Confirm flow now validates pending actions through `AgentContractValidator.ValidatePendingConfirmation`.
- Flags-off behavior remains preview-only.
- `AgentRunResponse.previewOnly` defaults to `true`.
- `AgentRunResponse.wroteData` defaults to `false`.
- `AgentRunResponse.createdResourceId` defaults to `null`.
- Pending action stores still default to preview-only semantics.
- The real-write branch remains behind existing feature gates:
  - `ENABLE_AGENT_WRITE_TOOLS`
  - `ENABLE_CREATE_LIFE_EVENT_TOOL`
- No Cloud Run env was changed.
- No Firestore Rules were changed.
- Real write was not enabled during this gate.

Important boundary:

- Existing real-write code remains present for Phase 5 gated write behavior, but this readiness gate does not enable it.

## 4. Smoke / Production Verification

Known completed verification in the current local workspace:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`: passed, 221 tests
- `git diff --check`: passed
- Agent Execution Contract Engine v1 local contract tests: passed

Production / deployment verification status:

| Check | Status | Notes |
| --- | --- | --- |
| preview-only API deployment | unknown in this gate | no deployment was performed |
| authenticated preview-only smoke on latest revision | blocker | must pass before Phase 6 implementation |
| life_event planner fix deployed and verified | unknown in this gate | no deployment was performed |
| `smoke-agent-life-event-write` | unknown in this gate | do not run real-write smoke without explicit approval |
| `smoke-rag-e2e` | unknown in this gate | not run in this gate |

Phase 6 blocker:

- Authenticated preview-only smoke has not been verified on the latest deployed revision after Execution Contract Engine v1.

## 5. Memory Engine Prerequisite Risks

Phase 6 Memory Engine will depend on these capabilities:

- stable intent routing so memory requests do not drift into life event writes
- clear `memory` vs `life_event` boundary
- memory proposed actions requiring confirmation before write
- memory update / merge / delete strategy
- memory pollution prevention, including rejecting low-confidence or user-hostile extracted facts
- capability boundary for any future memory read/write tools
- audit log, rollback, and deletion strategy
- deterministic contract tests for memory read, memory write preview, confirmation, cancel, and invalid payload paths

Current risk:

- The `memory` intent currently produces `save_memory_preview`, but no Memory Engine data model or persistence layer exists yet. That is correct for this gate. Phase 6 implementation must not reuse ad hoc payloads as durable memory schema without a separate design.

## 6. MCP / Capability Boundary

Current MCP posture:

- No new MCP was added.
- `codebase-memory-mcp` should not be formally connected at this stage.
- Existing MCP capabilities must not be automatically exposed to the Agent planner.
- Any future write-capable tool must be controlled by confirmation, feature gates, and contract validation.
- MCP tools should be treated as external capabilities, not implicit Agent actions.

## 7. Phase 6 Entry Decision

Decision: **B. Ready for Phase 6 design only**

The system is ready to start Phase 6 design work because:

- Agent Execution Contract Engine v1 exists.
- intent coverage is matrixed and tested locally.
- fallback is structured.
- response contract is unified.
- confirm flow now uses contract validation.
- local test suite passes.

The system is not ready for Phase 6 implementation because:

- latest deployed revision has not been verified with authenticated preview-only smoke after the engine refactor.
- no Phase 6 memory data model exists.
- no memory write confirmation contract has been designed beyond preview action shape.
- no memory update / merge / delete strategy exists.
- no memory pollution prevention strategy exists.
- no capability boundary for future memory tools has been formalized beyond current MCP caution.

## Blockers Before Phase 6 Implementation

1. Run authenticated preview-only smoke against the latest deployed API revision.
2. Confirm `life_event` planner behavior is deployed and verified on the latest revision.
3. Run RAG E2E smoke on the latest revision.
4. Write Phase 6 Memory Engine design before any code implementation.
5. Define memory schema, confidence model, merge/update/delete behavior, and rollback/audit strategy.
6. Define capability boundaries for future memory tools and any MCP-adjacent capabilities.
7. Keep real writes disabled unless a separate Release Gate explicitly approves canary or production enablement.

## Final Readiness Summary

- Phase 6 implementation: not ready.
- Phase 6 design: ready.
- Real writes: still disabled by default.
- Cloud Run env: unchanged.
- Firestore Rules: unchanged.
- MCP configuration: unchanged.
- Deployment: not performed.

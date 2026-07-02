# Phase 7.0 Runtime Tooling Architecture

Date: 2026-07-02

## Scope

Phase 7.0 is a design-only architecture phase. It documents the target runtime
tooling model for future Agent multi-tool expansion.

This document does not implement code, change API runtime behavior, change
frontend behavior, deploy services, modify Cloud Run environment variables,
modify Firestore Rules, modify MCP, enable durable memory writes, connect a real
Firestore Memory runtime, write `users/{userId}/memories`, write `life_events`,
or enable mock auth / mock LLM.

## 1. Phase 7 Goal

Phase 7 expands the Agent from a single preview / confirm capability into a safe multi-tool runtime.

The goal is to define how tools are declared, selected, gated, executed, traced, previewed, confirmed, audited, and eventually promoted through Release Gates.

Phase 7 is not:

- durable memory write enablement
- production memory repository rollout
- unrestricted autonomous agent behavior
- background auto-execution system
- direct production mutation without preview / confirm

## 2. Current Baseline

Current system capabilities:

- RAG chat / document QA
- Agent preview / confirm contract
- `create_life_event` preview-only write path
- Memory schema / proposal / retrieval / guard / extraction skeleton
- Read-only memory context provider
- Guarded memory proposal preview runtime
- Durable memory write disabled
- Real Firestore Memory runtime disconnected
- Frontend does not yet have full Memory UI / Dashboard

Current verified Phase 6 state:

- `dotnet test LifeAgent.Tests/LifeAgent.Tests.csproj`: 323 passed
- `scripts/smoke-memory-proposal-preview.mjs`: PASS
- Default-off memory proposal baseline: PASS
- Guard-enabled online checks: SKIP
- Memory proposal confirm remains `previewOnly=true` / `wroteData=false`

## 3. Tool Categories

### Read-only Tools

Examples:

- RAG retrieval
- memory retrieval
- calendar read
- document search
- status / diagnostics

Rules:

- May execute directly after eligibility and feature gate checks.
- Must emit a user-visible or auditable trace.
- Must not write user data.
- Must not create pending write actions.
- Must not silently upgrade into a mutating tool.

### Preview-only Mutating Tools

Examples:

- `create_life_event` preview
- `save_memory_preview`
- future calendar event preview
- future document update preview

Rules:

- May generate a pending action.
- Must require user confirmation before any durable write.
- Must default to no-write.
- Must be feature gated.
- Must expose a clear preview summary and risk level.

### Durable Mutating Tools

Examples:

- confirmed `life_event` write
- confirmed memory write
- calendar create / update / delete
- document write / update

Rules:

- Must pass a separate Release Gate.
- Must be controlled by feature flags.
- Must have idempotency.
- Must have audit.
- Must have rollback.
- Must require explicit user confirmation.
- Must never run from Planner output alone.

### Forbidden / Out-of-scope Tools

Examples:

- silent background write
- cross-user data access
- unconfirmed durable mutation
- unrestricted shell execution
- direct production env mutation

Rules:

- Must not be registered as runtime tools.
- Must not be exposed through Planner selection.
- Must not be made available through feature flag shortcuts.

## 4. Tool Registry Design

The Tool Registry declares tool capabilities. A registry entry should include:

- tool name
- tool category
- input schema
- output schema
- risk level
- `requiresConfirmation`
- `supportsPreview`
- `durableWriteCapable`
- feature flags
- owner service
- audit requirements

The registry is declarative. It is not an enablement mechanism by itself.

Important boundaries:

- Registry presence does not mean a tool is runtime-available.
- Feature flags decide whether a tool can be used.
- Category and risk level decide the required safety path.
- Durable write capability does not imply durable write is enabled.

## 5. Planner Contract

When the Planner selects a tool, it must produce:

- intended tool
- reason
- confidence
- risk level
- whether preview is required
- whether confirmation is required
- user-visible explanation
- fallback when unsafe

Planner restrictions:

- Planner cannot directly execute durable writes.
- Planner cannot bypass feature gates.
- Planner cannot promote a read-only tool into a mutating tool.
- Planner cannot create a durable resource id by itself.
- Planner must ask, no-op, or fall back when confidence is low.

## 6. Runtime Execution Flow

Unified flow:

```text
User request
-> Agent Planner
-> Tool eligibility check
-> Feature gate check
-> Risk classification
-> Read-only execute or preview action creation
-> User-visible trace
-> Optional confirm
-> Write coordinator if allowed
-> Audit / result
```

Read-only flow:

- Planner selects a read-only tool.
- Runtime checks tool eligibility and feature flags.
- Tool executes without creating pending actions.
- Runtime records trace and returns result.
- `wroteData` remains false.

Preview-only flow:

- Planner selects a preview-capable mutating tool.
- Runtime checks feature flags, validation, and guard results.
- Runtime creates a pending action.
- User sees preview and trace.
- No durable write occurs.

Confirm flow:

- User confirms an existing pending action.
- Runtime validates action ownership, status, expiration, and idempotency.
- If the write path is not release-gated on, confirm remains preview-only.
- If a future Release Gate allows write, the write coordinator performs the durable mutation.

Blocked flow:

- Runtime blocks when the tool is unavailable, feature-gated off, invalid, unsafe, cross-user, expired, or too risky.
- Runtime returns a user-visible reason.
- No pending write action or durable write occurs unless explicitly allowed by the category and gate.

Failure flow:

- Tool failures return structured diagnostics.
- Read-only failures fall back to no-tool behavior where possible.
- Preview failures return no pending action unless a safe partial preview is explicitly supported.
- Durable write failures must not be retried unsafely without idempotency.

## 7. Preview / Confirm Contract

Phase 7 should unify the existing preview / confirm contract across tools:

- `previewOnly`
- `wroteData`
- `createdResourceId`
- `actionType`
- `requiresConfirmation`
- `pendingActionId`
- `expiresAt`
- `validationResult`
- `guardResult`
- `userVisibleSummary`

Contract rules:

- Default-off serialized contract must remain stable.
- No hidden write is allowed.
- Confirm must be idempotent.
- Expired actions must not write.
- Cancelled actions must not write.
- Invalid actions must not write.
- Cross-user actions must not write.

## 8. Execution Trace

Future frontend trace should explain what the Agent did without exposing sensitive internals.

Trace fields may include:

- tool considered
- tool selected
- why selected
- safety check result
- preview generated
- confirmation required
- write skipped / write executed
- error / fallback reason

Trace restrictions:

- Must not leak tokens.
- Must not leak internal credentials.
- Must not output prompt secrets.
- Must avoid exposing sensitive memory or document content in logs by default.
- Should help users understand Agent behavior and safety decisions.

## 9. Error Handling and Fallback

The runtime must handle:

- tool unavailable
- feature flag off
- validation failed
- guard blocked
- auth failed
- external service failed
- partial failure
- timeout
- confirm expired
- idempotency conflict

Required behavior:

- safe fallback
- no accidental durable write
- user-visible explanation
- structured diagnostics
- no silent mutation
- no auto-escalation from read-only to mutating behavior

## 10. Security Boundary

Security boundaries:

- `userId` is the trust boundary for user data.
- Auth is required for user data access.
- Cross-user access is forbidden.
- Direct client-side durable mutation is forbidden without server validation.
- Mock auth must not be enabled in production.
- Durable writes must not happen without a Release Gate.
- Runtime Agent must not mutate env, deployment, Cloud Run, Firestore Rules, or MCP.
- Tools must not accept caller-supplied user ids for protected resources.
- Tool outputs must not leak credentials or internal secrets.

## 11. Feature Flag Strategy

Existing flags:

- `ENABLE_AGENT_WRITE_TOOLS`
- `ENABLE_CREATE_LIFE_EVENT_TOOL`
- `ENABLE_MEMORY_RETRIEVAL`
- `ENABLE_MEMORY_CONTEXT_IN_AGENT`
- `ENABLE_MEMORY_PROPOSAL_RUNTIME`
- `ENABLE_MEMORY_PROPOSAL_GUARD`

Future recommended flags:

- `ENABLE_TOOL_REGISTRY`
- `ENABLE_RUNTIME_TOOL_TRACE`
- `ENABLE_READONLY_TOOL_RUNTIME`
- `ENABLE_MUTATING_TOOL_PREVIEW`
- `ENABLE_DURABLE_TOOL_WRITE`
- `ENABLE_CALENDAR_READ_TOOL`
- `ENABLE_CALENDAR_WRITE_TOOL`

Strategy:

- Default false.
- Preview before write.
- Canary before production.
- Read-only before mutating.
- Tool-specific flags before broad class flags.
- Durable writes require explicit Release Gate approval.

## 12. Frontend Impact

Future Phase 7 frontend work may need:

- tool execution trace panel
- pending action card
- preview / confirm UI
- tool result display
- blocked action message
- memory proposal card
- future memory dashboard handoff

Phase 7.0 does not implement frontend changes. It only defines architecture and expected future surfaces.

## 13. Proposed Phase 7 Breakdown

- Phase 7.0 Runtime Tooling Architecture
- Phase 7.1 Tool Registry Skeleton
- Phase 7.2 Read-only Tool Runtime
- Phase 7.3 Tool Execution Trace Contract
- Phase 7.4 Preview Action Unification
- Phase 7.5 Frontend Pending Action / Trace UI Design
- Phase 7.6 Calendar / External Tool Read-only Pilot
- Release Gate: Durable Tool Write Enablement

## 14. Acceptance Criteria

Phase 7.0 is complete when:

- Runtime tool architecture is documented.
- Read-only / preview-only / durable mutating categories are defined.
- Planner feature gate and confirmation boundaries are explicit.
- Planner cannot bypass confirmation or feature gates.
- Frontend trace direction is defined.
- Future Phase 7 breakdown is documented.
- Durable writes remain behind a separate Release Gate.

## 15. Final Conclusion

Phase 7.0 establishes the runtime tooling architecture for safe multi-tool Agent expansion. It does not enable durable writes, production memory persistence, or autonomous mutation. All mutating capabilities remain gated by preview / confirm, feature flags, audit, and future Release Gates.

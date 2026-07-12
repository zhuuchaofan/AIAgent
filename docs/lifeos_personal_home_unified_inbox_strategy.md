# LifeOS Personal Home Unified Inbox Strategy

Date: 2026-07-11

## Principle

LifeOS Personal Home should use one daily input surface:

> 首页入口合一，风险分流；用户心智简单，系统执行保守。

The user should not need to choose between separate developer-facing tools for
life notes, reminders, memory, or agent actions. The system should classify the
input internally, show the planned action clearly, and keep execution conservative
until the relevant write path is explicitly approved.

## Current v1.3 Scope

The current short-term behavior is:

- The home page keeps one main input.
- The input copy is `记录生活，或创建一个提醒...`.
- Submitting creates a pending action through `/api/agent/pending-actions`.
- Pending action cards show a user-visible type:
  - `生活记录`
  - `提醒`
  - `计划`
- Confirm changes pending action state only.
- Confirm does not execute a tool.
- Confirm does not write `life_events`.
- Confirm does not write reminders.
- Confirm does not write memories.

The current preview action types are:

| User-facing type | Internal preview action type | Current confirm behavior |
|---|---|---|
| 生活记录 | `life_record_preview` | Mark confirmed only |
| 提醒 | `reminder_preview` | Mark confirmed only |
| 计划 | `plan_preview` | Mark confirmed only |

## Current Implementation Model

The current implementation has moved beyond a plain preview form, but remains
preview-only by default.

### Intent routing

Personal Home input now goes through an internal intent router before the
pending action is stored.

The frontend does not choose or submit `actionType` for normal home input. It
submits the user's text to `/api/agent/pending-actions`; the backend router is
the authority for classifying the input.

Current routed intents:

| Intent | Current source | Risk | Disposition | Pending action |
|---|---|---|---|---|
| `life_record` | default non-reminder home input | `low_record` | `pending_confirmation` | required |
| `reminder` | reminder-like home input | `medium_user_state_change` | `pending_confirmation` | required |
| `plan` | plan-like home input | `medium_user_state_change` | `pending_confirmation` | required |
| `tool_action` | unknown explicit action type | `high_external_or_irreversible` | `required_confirmation` | required |

Important current boundary:

- The home UI still exposes only one input box.
- The home UI does not expose a separate plan composer.
- The router may infer `plan_preview`, but it remains a pending preview action.
- Unknown explicit action types are preserved and routed conservatively as
  high-risk tool actions.

### Default-off policies

The runtime has explicit policy objects, but they default to conservative
preview behavior:

- `Phase80PersonalHomeRoutingPolicy.DefaultPreviewOnly()`
  - does not direct-save low-risk records.
  - keeps home submissions in pending confirmation.
- `Phase80ConfirmWritePolicy.DefaultPreviewOnly()`
  - does not allow `life_events` writes.
  - does not allow reminder writes.

These policies are code-level extension points only. They are not wired to
production environment variables, and they do not execute writes.

### Confirm and Memory plans

Each pending action now carries a creation-time snapshot of its confirm and
memory plans.

Confirm plan snapshot:

- `confirmTarget`
- `confirmWriteEnabled`
- `confirmWriteExecutionReady`
- `confirmWriteRealPathReady`
- `confirmWriteDecisionReason`
- `memoryCandidateOnly`
- `confirmPlanReason`

Memory plan snapshot:

- `memoryTarget`
- `memoryWriteEnabled`
- `memoryRequiresDedupe`
- `memoryRequiresMerge`
- `memoryRequiresConfirmation`

Current behavior:

- `life_record_preview` targets `life_events`, but write is disabled.
- `reminder_preview` targets `reminders`, but write is disabled.
- `plan_preview` targets no durable planning store yet, and write is disabled.
- `confirmWriteEnabled` means only that policy would allow the target.
- `confirmWriteExecutionReady` and `confirmWriteRealPathReady` remain false
  unless an explicit confirm write executor is connected.
- The default `Phase80NoOpConfirmWriteExecutor` never writes. It reports
  `confirm_write_policy_enabled_but_executor_not_connected` when policy is
  enabled but no real executor has been installed.
- The confirm write executor contract also defines `ExecuteAsync`, but the
  current Confirm path does not call it. The default no-op executor would return
  `status=skipped`, `wroteData=false`, and `realWritePath=false`.
- Memory target is `memory_candidate`.
- Memory write is disabled.
- Memory requires de-duplication, merge review, and confirmation before any
  future durable memory write.

The snapshot is intentionally stored at pending-action creation time so later
policy changes do not rewrite the meaning of existing pending actions.

### Safety details

The API view exposes route and plan audit fields:

- `intent`
- `disposition`
- `riskLevel`
- `requiresPendingAction`
- `routeReason`
- confirm plan fields
- memory plan fields

The frontend keeps these under `技术与安全详情`, so the default home view stays
focused on the user's next action rather than implementation details.

## Beta Direction

Beta should evolve from preview confirmation to selected real writes only after a
separate approval and release gate.

Allowed future Beta target:

- `life_record_preview` confirm may write `life_events`.
- `reminder_preview` confirm may write reminders.
- Memory still must not write directly from this path.
- Memory may produce a candidate/proposal only, with merge/conflict/pollution
  guard and explicit later confirmation.

Beta implementation must preserve these constraints:

- One home input remains the user-facing entry.
- Each generated pending action must show its type before confirmation.
- The confirm response must state whether a real write happened.
- The write path must be type-specific and auditable.
- Real writes require all three gates:
  - `confirmWriteEnabled=true` from policy.
  - `confirmWriteExecutionReady=true` and `confirmWriteRealPathReady=true` from
    the explicit confirm write executor.
  - the release gate below has been approved.
- Runtime executor invocation is governed by `Phase80ConfirmWriteInvocationGate`:
  - `confirm_write_policy_disabled`: do not invoke executor.
  - `confirm_write_executor_not_ready`: do not invoke executor.
  - `confirm_write_all_gates_ready`: executor may be invoked by an intentionally
    connected Beta runtime path.
- When runtime execution is intentionally connected in Beta, executor results
  must report:
  - `success`
  - `status`
  - `target`
  - `resourcePath`
  - `wroteData`
  - `realWritePath`
  - `executorId`
  - `reason`
- Low-risk direct-save behavior, if introduced later, must be guarded by a
  separate product and safety decision.

## Required Release Gate Before Real Writes

Before enabling confirm-to-write for either `life_events` or reminders:

1. User explicitly approves the specific write path.
2. Cloud Run environment variables are reviewed without accidental changes.
3. Real-write feature flags remain default-off until the gate is approved.
4. Tests prove `executed`, `wroteData`, `realWritePath`, and audit fields match
   actual behavior.
5. Firestore or storage rules changes, if any, are reviewed separately.
6. Rollback behavior is documented.
7. Production smoke checks prove no Memory writes occur from this path.

Do not enable durable Memory writes as part of this Beta path.

## Long-Term Agent Direction

Long-term, the one input becomes the LifeOS inbox:

- All input enters an intent router.
- Low-risk records may eventually save directly.
- Medium-risk reminders and plans require confirmation.
- High-risk tool calls always require confirmation.
- Memory is created only through candidate generation, de-duplication, conflict
  review, and explicit confirmation.

The frontend should stay product-oriented. Technical labels such as runtime,
phase, fake-first, legacy confirm, and store details belong in collapsed safety
details or project documentation, not the default home view.

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


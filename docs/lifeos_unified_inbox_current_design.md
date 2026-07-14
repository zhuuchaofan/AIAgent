# LifeOS Unified Inbox Current Design

Date: 2026-07-12

## Purpose

This document is the current-state source of truth for the LifeOS home input
and pending action flow. Older Phase 8 / Phase 9 documents describe the
preview-only skeleton at the time those phases were built. They remain useful
as historical safety references, but they no longer fully describe the deployed
behavior.

## Product Name

Current user-facing mainline:

```text
LifeOS Unified Inbox / 生活收件箱
```

The home input is not a complete autonomous Agent. It is a controlled inbox for
turning user text into server-side pending actions.

## Current Runtime Flow

```text
User input
  -> AgentPreview / 生活收件箱 UI
  -> POST /api/agent/pending-actions
  -> Unified Inbox intent classifier
  -> UnifiedInboxRuntime
  -> Phase80PendingActionRuntime compatibility core
  -> server-side pending action
  -> user Confirm / Cancel
  -> confirm gate
  -> allowed executor only
```

The server loads and confirms a stored pending action by id. Confirm does not
trust a frontend-resubmitted executable payload.

## Intent Classification

Production DI now uses:

```text
IUnifiedInboxIntentClassifier
  -> LlmUnifiedInboxIntentClassifier
  -> dedicated JSON-only intent prompt through IRagAnswerGenerator
  -> rule fallback on invalid JSON / unsupported action / LLM failure
```

The classifier is allowed to choose a candidate action type. It is not allowed
to write data or execute tools.

Classifier outputs map to the existing pending action types:

| Intent | Action type | Current write behavior |
|---|---|---|
| `life_record` | `life_record_preview` | Confirm writes `life_events` |
| `reminder` | `reminder_preview` | Confirm writes `reminders` when release gate is enabled |
| `plan` | `plan_preview` | Preview-only |
| unknown / external action | custom action type | No execution |

Explicit requested action types remain supported for tests and compatibility,
but the home input should normally let the classifier choose.

## Confirm / Write Gate

The real writes currently allowed through the Unified Inbox path are:

```text
life_record_preview
  -> Confirm
  -> Phase80LifeEventConfirmWriteExecutor
  -> users/{userId}/life_events/{eventId}

reminder_preview
  -> Confirm
  -> Phase80ReminderConfirmWriteExecutor
  -> users/{userId}/reminders/{reminderId}
     only when AllowReminderWrites=true and a concrete due time exists
```

Code defaults in `Program.cs` intentionally keep reminder writes closed unless
the release gate explicitly enables them:

```text
AllowLifeEventWrites = true
AllowReminderWrites = false by default; true in the current Reminder Write Release Gate deployment
MemoryWriteEnabled = false
Tool execution = unavailable
```

This means:

- Life record Confirm writes `life_events`.
- Reminder Confirm can write `reminders` in the dedicated release-gate
  deployment, but missing-time reminders remain pending-only and do not create
  timeless reminders.
- Memory remains candidate-only.
- External tools and MCP-style side effects remain closed.

`AllowReminderWrites` is controlled by configuration and defaults to false:

```text
UnifiedInbox:ConfirmWrites:AllowReminderWrites
UNIFIED_INBOX_ALLOW_REMINDER_WRITES
```

Only a dedicated Reminder Write Release Gate should set it to true.

## Data Sources

The home page has two separate data surfaces:

1. Pending action history
   - Source: `users/{userId}/pendingActions`
   - Shows pending / confirmed / cancelled / expired decisions.
   - Includes audit flags such as `wroteData`, `executed`, `realWritePath`,
     and `intentClassifier`.

Reminder items are intentionally kept off the home page's primary flow after
confirmation. Durable pending reminders can be reviewed and completed/cancelled
on `/reminders`.

2. Recent life records
   - Source: `users/{userId}/life_events`
   - Reflects confirmed life records after write succeeds.
   - Does not show confirmed reminders; reminders live on `/reminders`.

3. Memory preview surfaces
   - Source: recent `life_events` only.
   - `GET /api/memory/insights/preview` powers the Home `AI 发现` card.
   - `GET /api/memory/review-inbox/preview` powers `/memory/review`.
   - `POST /api/memory/review-inbox/{candidateId}/keep|dismiss` persists
     Review Inbox status to `users/{userId}/memory_review_items`.
   - `POST /api/memory/review-inbox/{candidateId}/remember` writes an edited,
     user-confirmed durable Memory from a kept candidate.
   - `GET /api/memory/items` powers `/memory`, the user's confirmed memories.
   - `POST /api/memory/items/{memoryId}/archive` powers first-version forget.
   - `GET /api/memory/context/preview` exposes read-only context for product
     validation and RAG background use.
   - Review Inbox actions such as keep, inspect source, and hide are product
     UI affordances; only the explicit `remember` action persists durable
     Memory records.

4. Life Q&A
   - Source: recent `life_events`, pending `reminders`, plus active durable
     Memory.
   - `POST /api/life/chat` answers questions about the user's life in
     read-only mode.
   - The web UI may tell the user when active remembered content or pending
     reminders were used as background.
   - It does not persist chat history, write Memory, create reminders, or
     execute tools.

5. Life review
   - Source: recent `life_events` plus active durable Memory.
   - `POST /api/life/review` returns structured review cards and source event
     references in read-only mode.
   - Review requests support `recent`, `today`, and `week` windows.
   - `/life/review` may let the user expand a card to inspect supporting life
     records.
   - `POST /api/life/review/cards/keep` may place a review card into Memory
     Review Inbox as a kept candidate.
   - It does not persist generated summaries, write Memory, create reminders,
     or execute tools.

## Legacy Paths

These routes still exist for compatibility and older tests:

```text
POST /api/agent/run
POST /api/agent/confirm
```

They are not the LifeOS Unified Inbox mainline.

The old frontend wrappers for `/api/agent/run` and `/api/agent/confirm`, the
manual `IngestForm` home UI, and the `/debug` pending action diagnostics page
were removed from the current web product surface after home productization.
This does not remove the backend compatibility routes or audit fields.

The old `/api/agent/pending-actions/demo` aliases were removed from the
current API surface. Historical Phase 8 / Phase 9 docs may still mention them
as deployment snapshots.

The mainline routes are:

```text
POST /api/agent/pending-actions
GET  /api/agent/pending-actions
POST /api/agent/pending-actions/{actionId}/confirm
POST /api/agent/pending-actions/{actionId}/cancel
POST /api/agent/pending-actions/{actionId}/archive
```

## Safety Invariants

These remain non-negotiable:

- Authenticated user id comes from server auth context.
- Classifier output is only a candidate.
- Confirm references a server-side pending action.
- Confirm checks owner, status, expiry, policy, and executor readiness.
- Only allowlisted executors may write.
- Automatic Memory durable writes are not enabled.
- Memory review candidates are preview-only and may include source summaries
  from recent life records.
- Memory review status may persist only under `memory_review_items`; this is
  not a durable Memory write.
- Memory Review `remember` may write durable Memory only from a kept candidate
  after user confirmation and guard validation.
- Confirmed Memory can be listed and archived by the user.
- Life Q&A may use recent life records, pending reminders, and active Memory as
  read-only context.
- Life Q&A must not write data, create/modify/complete/cancel reminders, or
  execute tools.
- Life Q&A must exclude archived and expired Memory from ordinary context.
- Life Q&A may show product-level memory usage feedback, but should not expose
  Memory as citations or implementation details.
- Life review may summarize recent records and active Memory, but must not
  persist generated summaries.
- Life review evidence expansion may show supporting life records, but not
  prompts, raw JSON, backend flags, or hidden implementation details.
- Life review "worth remembering" may write Memory Review state only under
  `memory_review_items`; durable Memory still requires explicit review and
  remember confirmation.
- Knowledge-base Q&A may use active Memory as auxiliary background only; the UI
  may surface that background is available and link to `/memory`.
- Memory is never a citation source; document citations must still come from
  retrieved Chunks.
- Reminder durable writes are gated by `AllowReminderWrites`; reminder delivery
  and external notification are not enabled.
- Tool execution is not enabled.
- Cloud Run env changes require explicit approval.

## Known Gaps

The current implementation is now clearer, but still not the final Agent:

- `UnifiedInboxRuntime` is the product-named runtime entrypoint.
  `Phase80PendingActionRuntime` remains as the compatibility core behind it.
- Frontend action names now use Unified Inbox wording, but backend class names
  and some tests still retain Phase 8 / Phase 80 compatibility names.

## Recommended Next Cleanup

1. Keep Reminder Write Release Gate smoke current:
   - explicit reminder request -> confirmed reminder appears on `/reminders`
   - missing-time reminder -> no durable reminder
   - completed/cancelled reminders leave the pending list
2. Plan Reminder Delivery Gate separately before any scheduler, notification,
   or external side effect is enabled.

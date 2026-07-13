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
  -> ILlmService.ParseAsync
  -> rule fallback on LLM failure
```

The classifier is allowed to choose a candidate action type. It is not allowed
to write data or execute tools.

Classifier outputs map to the existing pending action types:

| Intent | Action type | Current write behavior |
|---|---|---|
| `life_record` | `life_record_preview` | Confirm writes `life_events` |
| `reminder` | `reminder_preview` | Preview-only |
| `plan` | `plan_preview` | Preview-only |
| unknown / external action | custom action type | No execution |

Explicit requested action types remain supported for tests and compatibility,
but the home input should normally let the classifier choose.

## Confirm / Write Gate

The only real write currently enabled through the Unified Inbox path is:

```text
life_record_preview
  -> Confirm
  -> Phase80LifeEventConfirmWriteExecutor
  -> users/{userId}/life_events/{eventId}
```

Production configuration in `Program.cs` intentionally keeps:

```text
AllowLifeEventWrites = true
AllowReminderWrites = false
MemoryWriteEnabled = false
Tool execution = unavailable
```

This means:

- Life record Confirm writes `life_events`.
- Reminder Confirm only confirms the pending action; it does not write
  `reminders`.
- Memory remains candidate-only.
- External tools and MCP-style side effects remain closed.

## Data Sources

The home page has two separate data surfaces:

1. Pending action history
   - Source: `users/{userId}/pendingActions`
   - Shows pending / confirmed / cancelled / expired decisions.
   - Includes audit flags such as `wroteData`, `executed`, `realWritePath`,
     and `intentClassifier`.

2. Recent life records
   - Source: `users/{userId}/life_events`
   - Reflects confirmed life records after write succeeds.
   - Does not show reminder preview confirmations unless a future reminder
     write path is approved and implemented.

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

## Legacy Paths

These routes still exist for compatibility and older tests:

```text
POST /api/agent/run
POST /api/agent/confirm
POST /api/agent/pending-actions/demo
```

They are not the LifeOS Unified Inbox mainline.

The old frontend wrappers for `/api/agent/run` and `/api/agent/confirm`, the
manual `IngestForm` home UI, and the `/debug` pending action diagnostics page
were removed from the current web product surface after home productization.
This does not remove the backend compatibility routes or audit fields.

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
- Knowledge-base Q&A may use active Memory as auxiliary background only; the UI
  may surface that background is available and link to `/memory`.
- Memory is never a citation source; document citations must still come from
  retrieved Chunks.
- Reminder durable writes are not enabled.
- Tool execution is not enabled.
- Cloud Run env changes require explicit approval.

## Known Gaps

The current implementation is now clearer, but still not the final Agent:

- The classifier reuses `ILlmService.ParseAsync`, which was originally built
  for life event parsing. A dedicated lightweight intent-classification prompt
  would be cleaner.
- `Phase80PendingActionRuntime` is still the class name for compatibility.
  A future cleanup should wrap or rename it to `UnifiedInboxRuntime`.
- Compatibility `/demo` endpoints remain available.
- Frontend action names now use Unified Inbox wording, but backend class names
  and some tests still retain Phase 8 / Phase 80 compatibility names.

## Recommended Next Cleanup

1. Extract a dedicated `UnifiedInboxRuntime` wrapper around
   `Phase80PendingActionRuntime`.
2. Replace the reused event parser with a dedicated JSON-only intent classifier.
3. Remove compatibility demo routes after dependent tests and docs are
   updated.
4. Add authenticated production smoke covering:
   - journal-like input with future time mention -> life record
   - explicit reminder request -> reminder preview
   - confirmed life record appears in recent life records
5. Update old Phase 8 / Phase 9 docs to point to this current-state document.

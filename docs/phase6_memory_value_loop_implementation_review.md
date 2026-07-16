# Phase 6 Memory Value Loop Implementation Review

Date: 2026-07-16

## Current Stage

LifeOS is currently in Phase 6: Memory Engine / Memory Value Loop.

The product has moved beyond "can record, can review, can remember" and is now
shaping a daily personal context loop: the home page can summarize what matters
today, explain why it matters, and route the user into the relevant review,
memory, reminder, or plan surface.

It is still not a fully autonomous personal Agent. Automatic Memory writes,
reminder delivery or scheduling, Tool Execution, external side effects, task
execution, calendar integration, and MCP remain closed.

Current local baseline:

```text
ee0f7fb 整理Phase 6实现回顾
```

## Implemented Capabilities

### Unified Inbox and confirmed writes

- The home input routes user text through the Unified Inbox mainline.
- Confirmed life records can write durable `life_events`.
- Confirmed reminders can write durable `reminders` only when the reminder
  write gate is enabled and a concrete due time exists.
- Confirmed plans can write durable `plan_signals`.
- Memory candidates are still preview-first; durable Memory write happens only
  when the user explicitly remembers a kept candidate.

### Home Daily Hub

- `GET /api/home/overview` provides a first-screen read model for recent life
  records, AI insights, Memory Review count, active Memory count, reminder
  summary, plan signal summary, today focus, daily brief, and context spine.
- Today focus is read-only and ranks overdue or near-term reminders,
  Memory-supported plan signals, repeated recent patterns, and grounded
  insights.
- Today focus now carries continuity metadata so the UI can group items into
  "now", "soon", and "review" buckets and route the user to reminders, plans,
  or recent review without writing data.
- The web surface keeps a short-lived tab-local cache, displays update
  awareness, supports manual refresh, preserves stale content when background
  refresh fails, and exposes expandable read-only evidence.

### Personal context and review surfaces

- `IPersonalContextService` centralizes read-only recent life records, pending
  reminders, and active non-expired memories for downstream personal-context
  surfaces.
- Life Q&A can answer from recent life records, pending reminders, and active
  memories while showing used-context feedback.
- Home and Life Review can route into Life Q&A with prefilled, user-confirmed
  read-only questions; the question is not sent automatically.
- Life Review can generate recent / today / week review cards, source evidence,
  recent themes, and continuity hints.
- Life Review can recognize a Home focus query and explain why the user arrived
  from a today-focus item.
- Memory Review Inbox separates stable, observing, likely one-off, and already
  remembered signals to reduce memory pollution.
- My Memory lists active durable memories, supports explicit edit/archive, and
  shows read-only quality hints for duplicates, expiring context, missing
  expiry, and overly generic content.

## Read / Write / No-Go Boundaries

Current durable writes:

```text
Unified Inbox life_record Confirm -> users/{userId}/life_events
Unified Inbox reminder Confirm -> users/{userId}/reminders, gated and due-time required
Unified Inbox plan Confirm -> users/{userId}/plan_signals
Memory Review kept candidate Remember -> users/{userId}/memories
Memory Review keep/dismiss -> users/{userId}/memory_review_items
```

Current read-only context:

```text
Home Overview
Life Q&A
Life Review
Memory insights preview
Memory context preview
Knowledge-base Q&A personal background
```

No-Go by default:

- No automatic durable Memory write.
- No background Memory extraction.
- No reminder delivery, notification scheduling, or calendar action.
- No Tool Execution or external side effects.
- No production env changes without explicit approval.
- No Firestore Rules changes without explicit approval.
- No push unless explicitly requested.

## User Experience Now

The current user-facing loop is:

```text
Record life
  -> confirm structured life record / reminder / plan when appropriate
  -> Home Daily Hub explains today's important context
  -> user follows focus into Reminders, Plans, Life Review, Life Q&A, or Memory
  -> Life Review and Life Q&A preserve where the user came from as a read-only clue
  -> user keeps or remembers only the signals they choose
  -> confirmed Memory improves later Home, Life Q&A, Review, and RAG context
```

This makes LifeOS feel more personal without crossing into autonomous execution.
The system can now surface "why this matters today" and "where to continue",
but the user still owns every durable memory and every meaningful write.

## Technical Architecture Snapshot

The current mainline remains Unified Inbox plus read-only personal context:

```text
Home input
  -> UnifiedInboxRuntime
  -> pending action preview
  -> user confirm
  -> gated write executor for life_events / reminders / plan_signals
```

```text
Home Overview / Life Q&A / Life Review / Memory Preview
  -> IPersonalContextService and related read services
  -> recent life_events, pending reminders, active memories, plan signals
  -> explainable read-only UI surfaces
  -> no tool execution and no automatic write
```

The `contextSpine` from Home Overview is the current bridge toward a reusable
personal context model: it groups recent threads, signals, next-best links, and
context counts so later personal-context surfaces can share the same
understanding instead of building isolated heuristics.

## Remaining Gaps

- Home and review surfaces are useful, but still mostly summarize and route;
  they do not yet maintain long-running goals or tasks.
- Reminder management persists reminders, but does not deliver notifications or
  schedule real-world actions.
- Memory quality hints are read-only; they do not merge, rewrite, or expire
  memories automatically.
- Life Q&A and RAG can use personal context, but they do not yet produce a
  durable cross-session plan or autonomous next action.
- Phase 7 runtime tooling exists as architecture and skeletons, not as an
  enabled production tool-execution path.
- Memory durable write enablement beyond explicit Review Inbox remember still
  requires a separate Release Gate.

## Recommended Next Phases

1. Continue Phase 6 polish around personal-context usefulness:
   make Home, Life Review, Life Q&A, Memory, reminders, and plans feel like one
   continuous loop rather than separate screens.
2. Add more explainability and confidence controls before increasing autonomy:
   every AI suggestion should show why it appears and what data it used.
3. Prepare Phase 7 read-only runtime integration:
   tool registry, planner contract, safe read-only adapters, and visible
   execution traces.
4. Keep durable Memory automation, reminder delivery, and external tool writes
   behind explicit Release Gates.
5. Only after the read-only runtime is observable and trusted, consider gated
   user-confirmed action execution.

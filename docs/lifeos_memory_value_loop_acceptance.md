# LifeOS Memory Value Loop Acceptance

Date: 2026-07-15

## Purpose

This checklist validates the current Memory Value Loop from the user's point of
view. It is a product acceptance path, not a new feature plan and not a release
gate for automatic Memory writes.

## Scope

Validate this loop:

```text
Home record
  -> recent life records
  -> AI findings
  -> Memory Review Inbox
  -> My Memory
  -> Life Q&A / Life Review uses remembered context
```

Do not validate or enable:

- automatic durable Memory writes
- reminder delivery or external notification
- Tool Execution
- MCP or external side effects
- Cloud Run env changes
- Firestore Rules changes

## Preconditions

- Tester is signed in with a real Google account.
- Current Web build includes the LifeOS app icon and product metadata.
- API health is normal.
- `life_events`, `memory_review_items`, and `memories` are accessed only through
  existing authenticated API paths.

## Acceptance Path

### 1. Record a life event

On the home page, enter a normal life note such as:

```text
今天骑车回来，路上不太热，心率也不高。
```

Expected:

- The primary input remains the main action.
- The saved item appears in recent life records.
- The page does not expose implementation fields such as `life_events`,
  `wroteData`, `previewOnly`, raw JSON, or runtime flags.

### 2. Review AI findings

On the home page, inspect `AI 发现`.

Expected:

- Findings are short and user-facing.
- Findings describe patterns or recent state, not backend mechanics.
- One-off notes should not be presented as certain long-term traits.

### 3. Inspect possible memories

Open `/memory/review`.

Expected:

- Candidates are grouped as pending, observing, or remembered.
- Candidate cards show user-facing labels such as `更稳定`, `观察中`, or
  `一次性`.
- A likely one-off event is presented as something to observe, not something
  LifeOS already believes as long-term memory.
- Source expansion shows life-record evidence, not prompts or backend payloads.

### 4. Keep and remember one candidate

Choose a useful candidate, click `先留着`, edit the wording if needed, then
click `确认记住`.

Expected:

- `先留着` survives page refresh as review state.
- `确认记住` creates durable Memory only after the explicit user action.
- The remembered item appears under `已记住` and in `/memory`.
- The system does not create unrelated memories automatically.

### 5. Use remembered context in Life Q&A

Open `/life/chat` and ask:

```text
结合我的记忆看最近状态
```

Expected:

- The answer may use active remembered content as private background.
- The UI can indicate remembered content was used.
- The answer should not expose Memory as a document citation or implementation
  detail.
- The chat remains read-only: no life record, reminder, memory, or tool action
  is created.

### 6. Use remembered context in recent review

Open `/life/review`.

Expected:

- Recent review can use life records and active memories as context.
- Review cards remain read-only.
- If a card is worth remembering, it can only be sent into Memory Review state;
  durable Memory still requires the explicit Memory Review remember flow.

## Pass Criteria

- The loop feels like a user workflow: record, review, decide, remember, ask.
- The user does not need to understand pending actions, runtime flags, raw
  collections, or backend write mechanics.
- Confirmed memories improve Q&A or review context without becoming citations.
- One-off events are not over-promoted into durable identity or preferences.
- No unapproved write target, scheduler, notification, tool, env, or rules
  change is involved.

## If It Fails

Classify failures by surface:

- **Product wording**: user sees implementation language or confusing labels.
- **Memory quality**: one-off events look too certain, or stable patterns are
  missing.
- **State persistence**: kept/remembered decisions disappear after refresh.
- **Context use**: Life Q&A or Life Review ignores active memories or exposes
  them incorrectly.
- **Safety regression**: any automatic write or external side effect appears.

Only fix the smallest surface needed for the failure. Do not use this checklist
as permission to enable automatic Memory writes or Tool Execution.

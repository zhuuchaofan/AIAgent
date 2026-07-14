# LifeOS Authenticated Production Smoke Checklist

Date: 2026-07-14

## Purpose

This checklist validates the current LifeOS Unified Inbox mainline in a real
signed-in browser session after deployment. It is not a Release Gate for new
write targets.

Current allowed writes:

- `life_record_preview` Confirm may write `users/{userId}/life_events`.
- Memory Review `remember` may write a user-confirmed durable Memory.

Still closed:

- Reminder durable writes.
- Automatic Memory writes.
- Tool Execution and external side effects.
- Firestore Rules or Cloud Run env changes.

## Pre-smoke Conditions

- Deployed API and Web revisions are healthy.
- `https://life.zhuchaofan.com/` loads.
- API `/health` returns healthy.
- Production env was not changed for this smoke.
- Firestore Rules were not changed for this smoke.
- Tester uses a real Google login session.

## Smoke Scenarios

### 1. Life Record Confirm

Input:

```text
今天骑车回来，心率还不错。
```

Expected:

- Home creates one pending action.
- The action is classified as a life record.
- Confirm succeeds.
- The record appears in "最近生活记录".
- No tool execution is shown or implied.

### 2. Explicit Reminder Stays Preview-only

Input:

```text
提醒我明天上午九点交材料。
```

Expected:

- Home creates one pending action.
- The action is classified as a reminder line.
- Confirm succeeds as pending-action confirmation only.
- No real reminder is created.
- It does not appear as a durable reminder surface.
- It may remain visible only as pending-action history / reminder line state.

### 3. Future-time Journal Stays Life Record

Input:

```text
下周这个时候应该就在去新疆的路上啦，最近一直在准备。
```

Expected:

- The input is treated as a life record, not an explicit reminder.
- Confirm writes to recent life records.
- The text appears naturally in the timeline.

### 4. Memory Review Still Requires Explicit Remember

Flow:

1. Open "可能值得记住的事".
2. Keep one candidate.
3. Open "我的记忆".
4. Use Remember only when the candidate is intentional and clean.

Expected:

- Keep/dismiss state survives refresh.
- Durable Memory is created only after explicit Remember.
- Archived Memory no longer contributes to ordinary Life Q&A context.

### 5. Life Q&A Uses Read-only Context

Prompt:

```text
我最近状态怎么样？
```

Expected:

- Answer uses recent life records and active memories.
- No new life event is created.
- No new Memory is created.
- No reminder or tool action is executed.

## Failure Stop Rules

Stop and investigate before opening any new Release Gate if:

- Reminder Confirm writes a durable reminder unexpectedly.
- Any tool or external action executes.
- Memory appears without explicit Remember.
- Life record Confirm succeeds but recent records do not update after refresh.
- Cross-user or unauthenticated access appears possible.
- Cloud Run logs show repeated 500s after the smoke.

## Result Template

```text
Date:
API revision:
Web revision:
Tester account:

Life record Confirm:
Explicit reminder preview-only:
Future-time journal:
Memory Review / Remember:
Life Q&A read-only:

Unexpected writes:
Console/runtime errors:
Cloud Run 500s:

Conclusion:
```

## Next Gate

If this smoke passes, the next meaningful product gate is Reminder Write Release
Gate. That gate must be planned separately and must not be bundled with this
smoke.

## Reminder Write Release Gate Smoke

This section applies only after `UNIFIED_INBOX_ALLOW_REMINDER_WRITES=true` is
explicitly enabled for a dedicated release-gate deployment. It is not the
default production smoke.

Preconditions:

- `UNIFIED_INBOX_ALLOW_LIFE_EVENT_WRITES` is unset or true.
- `UNIFIED_INBOX_ALLOW_REMINDER_WRITES=true` is explicitly enabled.
- Tool Execution remains unavailable.
- Memory automatic writes remain disabled.
- Firestore Rules are unchanged unless separately approved.

### A. Explicit Reminder Creates Durable Reminder

Input:

```text
提醒我明天上午九点交材料。
```

Expected:

- Home creates one pending action.
- The action is classified as a reminder.
- Confirm succeeds.
- The response indicates the reminder was saved.
- A pending reminder appears on `/reminders` after refresh.
- It does not create a life record unless separately entered as a life record.

### B. Missing Time Does Not Create Reminder

Input:

```text
以后提醒我买一本新书。
```

Expected:

- Home creates one pending action.
- Confirm does not write `reminders`.
- The user sees a clear "missing time" style message.
- No empty or timeless reminder appears on `/reminders` after refresh.

### C. Reminder State Machine Still Works

Flow:

1. Create a reminder with an explicit due time.
2. Mark it completed.
3. Create another reminder and cancel it.

Expected:

- Completed and cancelled reminders disappear from the pending reminder list.
- Completed/cancelled reminders cannot be reverted to pending.
- Completed/cancelled reminders cannot have `dueAt` edited.

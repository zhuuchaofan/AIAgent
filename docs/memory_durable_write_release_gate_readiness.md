# Memory Durable Write Release Gate Readiness

Date: 2026-07-13

## Status

This document now records the first approved minimal durable Memory write gate.
It only covers user-confirmed writes from the Memory Review Inbox.

Current production boundary:

- Home `AI 发现` is preview-only.
- `/memory/review` can keep, hide, and explicitly remember retained candidates.
- Durable Memory write is only allowed after the user clicks `记住`.
- The write path is limited to `users/{userId}/memories`.
- No Cloud Run env flag is changed by this document.
- Memory is not automatically injected into Agent runtime by this gate.

## Minimum Durable Memory Schema

The first durable Memory write gate should use the smallest auditable record:

| Field | Purpose |
|---|---|
| `id` | Server-generated memory id. |
| `userId` | Server auth context only; never accepted from LLM or client body. |
| `type` | One of preference, habit, goal, temporary_context, theme. |
| `content` | User-facing memory statement. |
| `sourceEventIds` | Life event ids that justify the memory. |
| `confidence` | Bounded confidence score from server-side proposal logic. |
| `status` | `active` or `archived`. |
| `createdAt` | Server timestamp. |
| `updatedAt` | Server timestamp. |

## Confirmation Rules

- Durable Memory write requires a dedicated "记住" user action.
- The client may reference a candidate id and submit edited `content`, but the
  server must load the candidate from server-owned Review Inbox state before
  writing.
- LLM output cannot set `userId`, Firestore path, status, or timestamps.
- Merge is not automatic in the first gate. Similar existing memories should
  require a separate review decision.
- Every write response must clearly return `previewOnly=false`, `wroteData=true`,
  and the created memory id.

## Forget / Undo Rules

- The user must be able to archive or forget a durable memory.
- Forget must not delete the source life events.
- Archived memories must not be injected into future Agent context.
- A later hard-delete policy can be designed separately.

## Release Gate Checklist

- [x] Dedicated user approval for durable Memory write gate.
- [ ] Firestore path and indexes reviewed.
- [x] Auth and ownership checks verified in the API boundary.
- [x] Local tests for create, duplicate review, guard block, and candidate status.
- [x] Preview-only insight/review behavior remains unchanged outside `记住`.
- [x] No Cloud Run env change required.
- [ ] Production smoke uses a dedicated test account and known cleanup path.
- [ ] Rollback plan restores preview-only behavior without data loss.

## Non-Goals

- Do not enable Reminder writes.
- Do not enable Tool Execution.
- Do not modify Firestore Rules in this readiness step.
- Do not inject durable Memory into Agent runtime before read behavior is tested.

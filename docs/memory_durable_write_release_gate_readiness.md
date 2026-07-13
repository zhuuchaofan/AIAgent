# Memory Durable Write Release Gate Readiness

Date: 2026-07-13

## Status

This is a readiness checklist only. It does not approve, deploy, or enable
durable Memory writes.

Current production boundary:

- Home `AI 发现` is preview-only.
- `/memory/review` is preview-only.
- Review Inbox keep / hide / source inspection are UI-only affordances.
- No durable Memory repository is wired to production.
- No Cloud Run env flag is changed by this document.

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
- The client may reference a candidate id, but the server must rebuild or load
  the candidate from server-owned data before writing.
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

- [ ] Dedicated user approval for durable Memory write gate.
- [ ] Firestore path and indexes reviewed.
- [ ] Auth and ownership checks verified.
- [ ] Local tests for create, duplicate review, archive, and unauthorized access.
- [ ] Preview-only behavior remains unchanged when the gate is off.
- [ ] Cloud Run env changes explicitly reviewed and reversible.
- [ ] Production smoke uses a dedicated test account and known cleanup path.
- [ ] Rollback plan restores preview-only behavior without data loss.

## Non-Goals

- Do not enable Reminder writes.
- Do not enable Tool Execution.
- Do not modify Firestore Rules in this readiness step.
- Do not inject durable Memory into Agent runtime before read behavior is tested.

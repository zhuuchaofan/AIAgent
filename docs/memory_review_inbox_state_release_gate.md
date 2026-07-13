# Memory Review Inbox State Release Gate

Date: 2026-07-13

## Purpose

This document records the approved minimal write boundary for the Memory Review
Inbox. It is not a durable Memory write gate.

## Approved Write

The Memory Review Inbox may persist user review state for generated candidate
signals:

```text
users/{userId}/memory_review_items/{candidateId}
```

Allowed statuses:

- `pending`
- `kept`
- `dismissed`

The stored record may include candidate metadata such as title, type, review
stage, source event ids, and timestamps. This exists so UI actions such as
`先留着` and `忽略` survive refresh and return visits.

## Explicit Non-Goals

This gate does not approve:

- durable `memories` writes
- automatic Memory merge or archive
- Reminder writes
- Tool Execution
- external side effects
- production environment variable changes
- Firestore Rules changes

## Current Product Meaning

`先留着` means the candidate stays in the Review Inbox for later review.

`忽略` means the candidate is hidden from the default Review Inbox preview.

Neither action means the system has remembered the fact as long-term Memory.

## API Surface

```text
GET  /api/memory/review-inbox/preview
POST /api/memory/review-inbox/{candidateId}/keep
POST /api/memory/review-inbox/{candidateId}/dismiss
```

The action endpoints rebuild the current server-side candidate list from recent
life events and only accept a candidate id. The frontend does not submit
candidate content to be persisted.

Response flags must continue to communicate:

```text
previewOnly = true
memoryWriteEnabled = false
wroteMemory = false
```

`wroteReviewState = true` only means the Review Inbox status record changed.

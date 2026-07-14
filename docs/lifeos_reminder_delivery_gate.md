# LifeOS Reminder Delivery Gate

Date: 2026-07-14

## Current State

LifeOS can save confirmed reminders to `users/{userId}/reminders` when the
Reminder Write Release Gate is enabled and the reminder has a concrete due
time.

The current product only shows pending reminders inside LifeOS. It does not
send system notifications, schedule background jobs, call external tools, or
integrate with calendars.

## Next Gate

Reminder delivery/scheduling is a separate Release Gate. It must not be bundled
with reminder creation, Life Q&A, Memory, or Tool Execution work.

A future delivery gate must define:

- the delivery channel, such as web push, email, Telegram, or another explicit
  user-approved channel;
- the scheduler or worker trigger, such as Cloud Scheduler, Cloud Tasks, or an
  internal polling job;
- an idempotent delivery record so a reminder is not sent repeatedly by
  accident;
- retry and failure handling;
- user-facing settings for enabling or disabling delivery;
- production smoke checks and rollback criteria.

Until that gate is explicitly approved, `/reminders` remains the only reminder
surface and reminders are visible only when the user opens LifeOS.

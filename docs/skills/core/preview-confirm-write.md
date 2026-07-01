# Preview Confirm Write Skill

## When to use

Use this for any Agent action, Memory proposal, life event creation, reminder creation, external tool call, or feature that can mutate user or production data.

## Goal

Preserve LifeOS's controlled write model: preview first, user confirmation second, real durable write only behind explicit gates and Release Gate approval when production is involved.

## Inputs

- Proposed action or write behavior.
- Current Phase and Release Gate status.
- Feature gate state.
- Agent confirmation and pending action code/docs.
- Firestore target collection and auth boundary.

## Process

1. Classify the behavior as read, compute, preview proposal, confirm preview-only, or durable write.
2. Ensure preview generates structured proposed data without durable mutation.
3. Ensure confirm is idempotent and can remain preview-only while gates are off.
4. Require feature gates for durable writes.
5. Require Release Gate approval before enabling real production writes.
6. Record result fields accurately, such as `previewOnly`, `wroteData`, and `createdResourceId`.

## Red lines

- Do not let LLM output directly write Firestore data.
- Do not let confirm bypass feature gates.
- Do not enable durable production writes by default.
- Do not treat service registration as production write enablement.
- Do not write Memory or `life_event` data from preview-only paths.
- Do not use frontend-provided `userId` for write paths.

## Done criteria

- Write capability is classified and gated.
- Preview-only and durable write semantics are distinct.
- Confirm behavior is idempotent.
- Production writes remain disabled unless Release Gate approved.

## Checklist

- [ ] Risk level classified.
- [ ] Preview payload has no durable write side effect.
- [ ] Confirm path handles disabled gates.
- [ ] Durable write path has feature gate checks.
- [ ] Backend injects trusted `userId`.
- [ ] Result fields accurately report write status.
- [ ] Release Gate required for production real writes.

## Related skills

- `core/safety-boundaries.md`
- `core/phase-management.md`
- `_shared/safety-red-lines.md`
- `_shared/phase-map.md`

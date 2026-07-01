# Project Glossary

## When to use

Use this shared glossary whenever a task mentions LifeOS concepts, Phase names, Agent actions, RAG, Memory, Firestore paths, deployment, or production write behavior.

## Goal

Provide stable project vocabulary so agents do not redefine terms during implementation, review, or documentation.

## Inputs

- User request.
- Relevant Phase docs under `docs/`.
- Current code paths in `LifeAgent.Api/` and `life-agent-web/`.
- Shared Phase source: `docs/skills/lifeos-phase-assessment.md`.

## Process

1. Normalize project terms before planning work.
2. Prefer existing project names over new synonyms.
3. Check whether a term describes design, preview-only behavior, development-complete behavior, or production-enabled behavior.
4. If a term is ambiguous, state the interpretation before making changes.

## Core terms

| Term | Meaning |
|---|---|
| LifeOS / LifeAgent | Personal life agent system, not only a chat app or RAG app. |
| Phase | Development stage with defined scope and done criteria. |
| Release Gate | Production enablement gate outside development Phases. |
| RAG | Phase 3 knowledge access layer: upload, chunk, embed, retrieve, answer, cite. |
| Agent Preview | UI and backend flow that proposes actions without directly writing data. |
| Agent Action | Structured proposed or executable action generated through Agent workflow. |
| `life_event` | Raw timeline data under `users/{userId}/life_events`. |
| Memory | Higher-level long-term cognitive entity, distinct from raw `life_event`. |
| Preview-only | Confirm path returns no real write; must not be described as production write-ready. |
| Feature Gate | Runtime control that keeps write capability disabled unless explicitly enabled. |
| Firebase Auth | Identity source; backend must verify token and inject trusted `userId`. |
| Firestore | User-scoped data store; paths must stay under `users/{userId}/...`. |
| Cloud Run | Production hosting for API and Web services. |

## Red lines

- Do not invent new Phase names when existing Phase definitions apply.
- Do not call RAG "real Agent" behavior.
- Do not call Memory design or preview proposal durable Memory implementation.
- Do not merge Release Gate work into a development Phase.
- Do not treat frontend, LLM, or request payload identity fields as trusted.

## Done criteria

- The task uses existing project vocabulary consistently.
- Design, preview-only, development-complete, and production-enabled states are separated.
- Any ambiguous term is clarified in the response or docs.

## Checklist

- [ ] Phase terms match `_shared/phase-map.md`.
- [ ] `life_event` and Memory are not conflated.
- [ ] Preview-only is not described as real production write.
- [ ] Release Gate remains separate from development Phase.
- [ ] Trusted identity source is backend-authenticated `userId`.

## Related skills

- `_shared/phase-map.md`
- `_shared/safety-red-lines.md`
- `core/phase-management.md`
- `core/preview-confirm-write.md`

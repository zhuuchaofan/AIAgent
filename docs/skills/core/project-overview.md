# Project Overview Skill

## When to use

Use this when a new agent enters the project, when a task lacks project context, or when deciding how a request fits LifeOS architecture.

## Goal

Establish the LifeOS / LifeAgent baseline: a personal life agent system built in Phases, with docs-first planning, controlled Agent actions, and strict production safety boundaries.

## Inputs

- User request.
- `docs/skills/lifeos-phase-assessment.md`
- `_shared/phase-map.md`
- Current repository structure.
- Relevant Phase docs.

## Process

1. Identify the product area: life data, RAG, Agent, Memory, auth, Firestore, frontend, deployment, or release.
2. Confirm current Phase and whether the task is design, implementation, validation, or Release Gate.
3. Inspect existing docs and code before proposing changes.
4. Use project-specific architecture instead of generic templates.
5. Keep the response explicit about what is done, planned, blocked, or gated.

## Project baseline

- Backend: .NET Minimal API with service/repository boundaries.
- Frontend: Next.js web app with Firebase Auth.
- Data: Firestore under user-scoped paths.
- Auth: Firebase ID token verified by backend.
- RAG: document upload, vector retrieval, citations, conversation history.
- Agent: controlled runner, read tools, pending actions, preview/confirm lifecycle.
- Memory: Phase 6 long-term memory design and preview-oriented skeleton work.
- Deployment: API and Web on Cloud Run.

## Red lines

- Do not treat LifeOS as only a chat app.
- Do not skip Phase classification before large changes.
- Do not modify production behavior while only doing project orientation.
- Do not assume historical chat context is available.

## Done criteria

- The agent can explain the current product shape and Phase context.
- The next action is routed to the right Skill.
- No code or production state changes occur during orientation unless requested.

## Checklist

- [ ] Product area identified.
- [ ] Current Phase checked.
- [ ] Existing docs/code inspected before edits.
- [ ] Safety boundary stated for risky tasks.
- [ ] Correct follow-on Skill selected.

## Related skills

- `_shared/project-glossary.md`
- `_shared/phase-map.md`
- `core/phase-management.md`
- `core/safety-boundaries.md`

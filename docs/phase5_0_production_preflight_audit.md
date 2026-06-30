# Phase 5.0 Production Enablement Preflight Audit

## Scope

Phase 5.0 is a read-only production enablement audit for the feature-gated Agent `create_life_event` write path.

No code, Cloud Run env, Firestore Rules, Firebase Auth, frontend, deployment, push, or real production write was performed.

## 1. Git State

Commands reviewed:

```bash
git status --branch --short
git status --short
git log --oneline -12
```

Current state:

- Branch: `main...origin/main`
- Ahead origin/main: `0` at the time of this audit.
- Working tree: clean before this audit document was added.
- Latest commit: `3989f1f docs: 增加 Phase 4.9 生产启用准备文档`

Recent local history includes Phase 4.8 and Phase 4.9:

```text
3989f1f docs: 增加 Phase 4.9 生产启用准备文档
68ba834 Phase 4.8.10 document life event write path closure
d738a76 Phase 4.8.9 add life event write DI safety tests
d7c2710 Phase 4.8.8 wire life event coordinator into confirm
7c6fb1b Phase 4.8.7 add idempotent life event write coordination
356cd66 feat: 接入 LifeEvent confirm 预览校验
9fb975b docs: 设计 LifeEvent confirm 写入流程
bfa383b feat: 增加 Firestore LifeEvent 写入服务骨架
d72f849 feat: 增加 Agent 写入 feature gate
2ba94d0 feat: 增加 LifeEvent payload 映射
4011e97 feat: 增加 LifeEvent 写入骨架
76e0b4a docs: 冻结 create_life_event 写入设计
```

## 2. Cloud Run API State

Read-only command:

```bash
gcloud run services describe life-agent-api \
  --region us-central1 \
  --project copper-affinity-467409-k7 \
  --format="yaml(status.url,status.traffic,spec.template.spec.containers[0].env)"
```

Observed API service:

- URL: `https://life-agent-api-hyo2yvwwia-uc.a.run.app`
- Active API revision: `life-agent-api-00033-gtt`
- Traffic: `100%` to `life-agent-api-00033-gtt`

Observed env:

```text
ASPNETCORE_ENVIRONMENT=Production
USE_MOCK_AUTH=false
USE_MOCK_LLM=false
FIRESTORE_PROJECT_ID=copper-affinity-467409-k7
FIREBASE_PROJECT_ID=my-agent-app-a5e42
```

Agent write flags:

- `ENABLE_AGENT_WRITE_TOOLS`: not set.
- `ENABLE_CREATE_LIFE_EVENT_TOOL`: not set.

Because `AgentWriteFeatureOptions` defaults unset values to false, production Agent writes remain disabled.

Conclusion:

- Current production API is still preview-only for Agent write enablement.
- No production real Agent `create_life_event` write has been enabled.

## 3. Firestore Rules State

Rules file found:

```text
./firestore.rules
```

Current `life_events` rule:

```text
match /users/{userId}/life_events/{eventId} {
  allow read: if isOwner(userId);
  allow create: if isOwner(userId)
    && hasRequiredString('type')
    && hasRequiredString('content')
    && hasRequiredString('title')
    && request.resource.data['userId'] == userId
    && request.resource.data['isDeleted'] == false
    && isNumberInRange('importance', 1, 5);
  allow update: if isOwner(userId)
    && request.resource.data['userId'] == resource.data['userId']
    && request.resource.data['createdAt'] == resource.data['createdAt'];
  allow delete: if isOwner(userId);
}
```

Findings:

- Rules cover `users/{userId}/life_events/{eventId}`.
- Users can read their own `life_events`.
- Current rules allow authenticated owners to create, update, and delete their own `life_events` directly from clients.
- This does not satisfy the Phase 5 production enablement goal of prohibiting client-side direct `life_events` writes while allowing backend Admin SDK writes.
- Firebase Auth project split is intentional in code:
  - Firestore project: `copper-affinity-467409-k7`
  - Firebase Auth project: `my-agent-app-a5e42`
  - `FirebaseApp` initializes with `FIREBASE_PROJECT_ID`.
  - Firestore Rules evaluate `request.auth.uid`, so cross-project token/rules behavior must be explicitly verified before enabling production writes.

Blocking status:

- Firestore Rules are a Phase 5.1 blocker before any production real Agent write enablement.

## 4. Firebase Auth / Cross-Project Risk

Current API Cloud Run env uses:

```text
FIRESTORE_PROJECT_ID=copper-affinity-467409-k7
FIREBASE_PROJECT_ID=my-agent-app-a5e42
```

Risk:

- API auth verification is configured against Firebase project `my-agent-app-a5e42`.
- Firestore data lives in project `copper-affinity-467409-k7`.
- Backend Admin SDK writes bypass Firestore Rules, but client access and rules still depend on `request.auth`.
- Before production write enablement, Phase 5.1 should explicitly verify that frontend Firebase Auth tokens map correctly to Firestore Rules in the target Firestore project.

Conclusion:

- Not an immediate blocker for backend Admin SDK write path while flags are off.
- A required preflight item before enabling production writes.

## 5. Life Events API / Frontend Readiness

Backend API:

- `GET /api/life/events` calls `LifeEventService.ListEventsAsync`.
- `LifeEventService.ListEventsAsync` reads from `users/{userId}/life_events`.
- The endpoint maps records to `TimelineEventDto`.
- `TimelineEventDto` includes `Source` and `StructuredData`.
- `TimelineEventDto` does not expose `CreatedBy` or `AgentActionId`.

Frontend:

- `life-agent-web/src/app/actions/events.ts` defines `LifeEvent` with `source?: string` and `structuredData?: Record<string, unknown>`.
- It does not currently model `createdBy` or `agentActionId`.
- `Timeline.tsx` renders `type`, `title`, `content`, `tags`, `importance`, `structuredData`, and timestamps.
- It does not depend on `createdBy` or `agentActionId`, so extra fields should not crash the UI.
- Agent-created events with `source=agent_confirmed` should display as normal timeline items if they pass existing DTO mapping and query filters.

Frontend readiness conclusion:

- No obvious UI crash blocker for displaying Agent-created `life_events`.
- Minimal UI compatibility is probably sufficient for read/display.
- Before production enablement, decide whether the UI should visibly mark `source=agent_confirmed` and whether editing Agent-created events should preserve audit metadata. This is not required for Phase 5.0 but should be considered before broad rollout.

## 6. Smoke Readiness

Reviewed script:

```text
scripts/smoke-rag-e2e.mjs
```

Current coverage:

- API `/health`.
- API root.
- Web root.
- Authenticated Agent Preview list documents when `FIREBASE_ID_TOKEN` is present.
- Agent proposed action for reminder preview.
- `/api/agent/confirm` preview-only confirmation.
- Idempotent preview-only confirm.
- Authenticated RAG upload/RAG/delete flow only when `RUN_MUTATING_SMOKE=true`.

Safety:

- If `API_BASE_URL` is missing, API/auth flow is skipped.
- If `FIREBASE_ID_TOKEN` is missing, authenticated RAG and Agent Preview flows are skipped.
- Mutating upload/RAG/delete flow requires explicit `RUN_MUTATING_SMOKE=true`.

Current gap:

- No true `create_life_event` write smoke exists.
- No flag-on write smoke exists.
- That is acceptable for Phase 5.0 because production writes must not be executed in this phase.

Smoke readiness conclusion:

- Current smoke is safe for preview-only validation.
- Phase 5.1 or later needs a dedicated gated real write smoke using a dedicated test user and explicit opt-in env var.

## 7. Rollback Readiness

Current rollback levers:

- Keep `ENABLE_AGENT_WRITE_TOOLS` unset or false.
- Keep `ENABLE_CREATE_LIFE_EVENT_TOOL` unset or false.
- If future production enablement is faulty, disable either flag.
- API traffic can be rolled back to a previous Cloud Run revision.

Current active API revision:

```text
life-agent-api-00033-gtt
```

Rollback gap:

- A documented production rollback runbook exists conceptually in Phase 4.8 closeout, but Phase 5.1 should freeze exact commands and expected post-rollback smoke checks before enabling writes.

## 8. Observability Gaps

Existing logging:

- `LifeEventService` logs manual event writes/list/update/delete operations.
- RAG and document endpoints have structured logs.
- Agent tool executor logs tool failures.

Observed Agent write-path gaps:

- `AgentEndpoints` does not log confirm `actionId`, `actionType`, feature gate result, `wroteData`, or `createdResourceId`.
- `AgentLifeEventConfirmationWriteCoordinator` does not log `invalid_payload`, `write_failed`, idempotent replay, or successful created resource.
- `FirestorePendingAgentActionStore` does not log cross-user reject/not_found/expired/confirmed transitions.

Observability blocker:

- This is not a hard blocker for local code readiness.
- It is a production enablement blocker for safe rollout. Phase 5.1 should add structured logs before enabling real writes.

## 9. Production Write Enablement Readiness

The codebase now has:

- A feature-gated `create_life_event` write path.
- Default false flags.
- Payload mapper and schema validator.
- Backend-derived user id and system fields.
- Stable event id from `agentActionId`.
- Pending action idempotency fields.
- Write failure handling that keeps pending action unconfirmed.
- Endpoint tests.
- Coordinator tests.
- DI safety tests.
- Documentation closure.

However, production enablement still has blockers:

1. Firestore Rules currently allow direct client creates/updates/deletes for `life_events`; this must be reviewed and changed or explicitly accepted with rationale before production write enablement.
2. Cross-project Firebase Auth / Firestore Rules behavior needs explicit verification.
3. Agent write-path structured logs are missing.
4. No dedicated flag-on real write smoke exists.
5. Rollback commands and post-rollback checks should be frozen before enablement.

## 10. Phase 5.1 Entry Recommendation

Recommended Phase 5.1 scope:

- Firestore Rules hardening design and test plan.
- Structured logging for Agent confirm/write path.
- Real write smoke design only, or local/test-only implementation with explicit opt-in.
- Rollback runbook.
- Firebase Auth / Firestore Rules cross-project verification.

Phase 5.1 should still not enable production real writes until the above blockers are resolved.

## 11. Final Conclusion

Production real Agent `create_life_event` writes must not be enabled yet.

Phase 5.0 audit conclusion:

```text
NOT READY TO ENABLE PRODUCTION REAL WRITES.
READY TO ENTER PHASE 5.1 PRODUCTION ENABLEMENT PREPARATION.
```

The system is safe in its current state because production write flags are unset/false and no deployment/env changes were made.

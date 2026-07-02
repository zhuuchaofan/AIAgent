# Phase 6.8A Dedicated Memory Proposal Default-off Smoke Result

## 1. Scope

This document records the Phase 6.8A dedicated memory proposal default-off smoke result.

This result only covers the dedicated default-off smoke attempt for `scripts/smoke-memory-proposal-preview.mjs`.

It does not mean:

- guard-enabled online smoke is complete
- durable Memory write is enabled
- real Firestore Memory runtime is connected
- Release Gate has passed

## 2. Baseline

- Smoke script commit: `4413a9be89f375340d9307aaaf28d2a6e85713bf`
- API_BASE_URL: `https://life-agent-api-151587524132.us-central1.run.app`
- Execution time: `2026-07-02 20:54:36 CST`
- Retry time with refreshed temporary token: `2026-07-02 20:58:26 CST`
- Second retry time with refreshed temporary token: `2026-07-02 21:04:44 CST`
- API revision during follow-up read-only check: `life-agent-api-00038-w9d`
- Revision timing: current online revision was created before the Phase 6.8A implementation and dedicated smoke script commits
- Durable memory write: not enabled
- Real Firestore Memory runtime: not connected
- Cloud Run env modified: no
- Firestore Rules modified: no
- MCP modified: no
- Deployed: no
- Pushed: no

## 3. Token Handling

- FIREBASE_ID_TOKEN: present
- Temporary token used: yes
- Full token recorded: no
- Fake token: no
- Mock auth: no
- Mock LLM: no

## 4. Smoke Result

Executed command shape:

```bash
FIREBASE_ID_TOKEN="[redacted temporary token]" \
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
node scripts/smoke-memory-proposal-preview.mjs
```

Result:

- `/health`: PASS
- Default-off baseline: FAIL
- Failure point: authenticated Agent run returned `401 UNAUTHORIZED`
- Error summary: `{"success":false,"error":{"code":"UNAUTHORIZED","message":"无效或过期的 Token"}}`
- Retry with refreshed temporary token: same `401 UNAUTHORIZED` result
- Second retry with refreshed temporary token: same `401 UNAUTHORIZED` result
- Guard-enabled checks: not reached
- Overall result: FAIL

## 5. Default-off Contract Verification

The default-off contract verification did not complete because the authenticated Agent run failed before a `save_memory_preview` response was returned.

- save_memory_preview returned: not verified
- proposedAction.requiresConfirmation: not verified
- payload.previewOnly: not verified
- serialized payload omits `guardDecision`: not verified
- serialized payload omits `blocked`: not verified
- serialized payload omits `reviewRequired`: not verified
- serialized payload omits `guardReason`: not verified
- serialized payload omits `conflictResult`: not verified
- serialized payload omits `mergeCandidate`: not verified
- confirm previewOnly: not verified
- confirm wroteData: not verified
- createdResourceId null or empty: not verified

## 6. No-write Verification

- wroteData=true observed: no
- users/{userId}/memories write: not directly queried; no write signal was observed before the authenticated run failed
- life_events write: no proposal runtime write signal observed
- durable memory write enabled: no
- extraction triggered: no
- automatic RAG/chat/background memory proposal: no

No Firestore direct query was performed by this script. Do not treat this result as a direct Firestore no-write audit.

## 7. Known Limits

- The script did not directly query Firestore.
- users/{userId}/memories no-write is based only on response behavior before failure and durable write remaining disabled.
- Guard-enabled online checks were not executed because the default-off authenticated flow failed first and expectation flags were not set.
- The supplied temporary token was present but rejected by the API as invalid or expired.
- A refreshed temporary token was also rejected by the API as invalid or expired.
- A second refreshed temporary token was also rejected by the API as invalid or expired.
- Follow-up read-only check found the token `iss` / `aud` matched `my-agent-app-a5e42`, Cloud Run env had `FIREBASE_PROJECT_ID=my-agent-app-a5e42`, and local current auth code also initializes Firebase Admin with `FIREBASE_PROJECT_ID=my-agent-app-a5e42`.
- The repeated 401 is therefore more likely caused by the online API revision / auth code version mismatch than by the temporary token itself.
- Continuing to rotate tokens is not recommended until the online API revision is updated or otherwise proven to run the current auth code.
- Changing auth code is not recommended based on the current local code review.

## 8. Final Conclusion

Phase 6.8A dedicated memory proposal default-off smoke failed. The failure point was authenticated Agent run returning `401 UNAUTHORIZED`.

The likely root cause is online API revision / auth code version mismatch. The recommended next step is separate user approval for a preview-only API redeploy.

Any redeploy must not modify Cloud Run env, must not enable mock auth, must not enable mock LLM, and must not enable write flags.

Durable memory write remains disabled. Real Firestore Memory runtime remains disconnected. Guard-enabled online smoke remains pending.

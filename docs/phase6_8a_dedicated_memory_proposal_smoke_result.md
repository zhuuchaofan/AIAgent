# Phase 6.8A Dedicated Memory Proposal Default-off Smoke Result

## 1. Scope

This document records the Phase 6.8A dedicated memory proposal default-off smoke result.

This result only verifies the default-off dedicated smoke path for `scripts/smoke-memory-proposal-preview.mjs`.

It does not mean:

- guard-enabled online smoke is complete
- durable Memory write is enabled
- real Firestore Memory runtime is connected
- Release Gate has passed

## 2. Historical 401 Investigation

Initial dedicated smoke attempts failed at the authenticated Agent request with `401 UNAUTHORIZED`.

At that time, the likely cause was suspected to be online API revision / auth code version mismatch because:

- token `iss` / `aud` matched `my-agent-app-a5e42`
- Cloud Run env had `FIREBASE_PROJECT_ID=my-agent-app-a5e42`
- local current auth code initialized Firebase Admin with `FIREBASE_PROJECT_ID=my-agent-app-a5e42`
- the then-online revision was older than the Phase 6.8A implementation and dedicated smoke script commits

Follow-up checks found a more specific root cause:

- failed token length observed by the smoke script: `1150`
- full token length verified by manual shell check: `1154`
- manual retry with the full-length token passed

Final corrected root cause: confirmed token copy truncation.

## 3. Token Handling

- FIREBASE_ID_TOKEN: present
- Temporary token used: yes
- Token length verified: `1154`
- Previous failed token length observed: `1150`
- Full token recorded: no
- Fake token: no
- Mock auth: no
- Mock LLM: no

## 4. Smoke Result

Executed command shape:

```bash
API_BASE_URL="https://life-agent-api-151587524132.us-central1.run.app" \
FIREBASE_ID_TOKEN="$TOKEN" \
node scripts/smoke-memory-proposal-preview.mjs
```

Result:

- `scripts/smoke-memory-proposal-preview.mjs`: PASS
- API `/health`: PASS
- Default-off memory proposal baseline: PASS
- Guard-enabled checks: SKIP
- Failures: none in the manual full-length-token retry

## 5. Default-off Contract Verification

- Explicit memory intent returned `save_memory_preview`: yes
- `proposedAction.requiresConfirmation=true`: yes
- `payload.previewOnly=true`: yes
- Serialized payload omitted `guardDecision`: yes
- Serialized payload omitted `blocked`: yes
- Serialized payload omitted `reviewRequired`: yes
- Serialized payload omitted `guardReason`: yes
- Serialized payload omitted `conflictResult`: yes
- Serialized payload omitted `mergeCandidate`: yes
- Confirm `previewOnly=true`: yes
- Confirm `wroteData=false`: yes
- `createdResourceId` null or empty: yes

## 6. No-write Verification

- `wroteData=true` observed: no
- Firestore direct memory write queried: no
- `users/{userId}/memories` write: not directly queried; inferred no-write from response contract and disabled durable write flags
- `life_events` write from proposal runtime: no observed signal
- Durable memory write: not enabled
- Extraction trigger: no
- RAG/chat/background auto memory proposal: no

No Firestore direct query was performed by this script. Do not treat this result as a direct Firestore no-write audit.

## 7. Environment / Deployment Boundary

- Push: no
- Deploy: no during the final manual smoke retry
- Cloud Run env modified: no
- Firestore Rules modified: no
- MCP modified: no
- Mock auth enabled: no
- Mock LLM enabled: no
- Durable memory write enabled: no
- Real Firestore Memory runtime connected: no

Note: a user-approved preview-only API redeploy was performed before the final manual retry to ensure the online API was running the current code. That redeploy did not modify Cloud Run env, did not enable mock auth, did not enable mock LLM, and did not enable write flags.

## 8. Known Limits

- The script did not directly query Firestore.
- `users/{userId}/memories` no-write is inferred from response contract and disabled durable write flags.
- Guard-enabled online checks were skipped because expectation flags were not set.
- Future guard-enabled smoke requires a separate preview-only / canary environment and separate approval.

## 9. Final Conclusion

Phase 6.8A dedicated memory proposal default-off smoke passed after retrying with a full-length temporary Firebase ID token. The previous 401 was caused by token copy truncation, not by confirmed API auth-code mismatch. Default-off serialized `save_memory_preview` payload remains unchanged. Confirm remains `previewOnly=true` / `wroteData=false`. Durable memory write remains disabled. Real Firestore Memory runtime remains disconnected. Guard-enabled online smoke remains pending.

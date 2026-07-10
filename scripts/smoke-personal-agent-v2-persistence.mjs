#!/usr/bin/env node

const config = {
  apiBaseUrl: trimTrailingSlash(process.env.API_BASE_URL || ""),
  token: process.env.FIREBASE_ID_TOKEN || "",
  tokenB: process.env.FIREBASE_ID_TOKEN_B || "",
  runPersistenceSmoke: isTrue(process.env.RUN_PERSONAL_AGENT_V2_PERSISTENCE_SMOKE),
  expectFirestore: isTrue(process.env.EXPECT_PERSONAL_AGENT_FIRESTORE_PERSISTENCE),
  prefix: process.env.SMOKE_TEST_PREFIX || "[SMOKE TEST]"
};

async function main() {
  console.log("LifeAgent Personal Agent v2 persistence smoke");
  console.log(`API: ${config.apiBaseUrl || "(not configured)"}`);
  console.log(`RUN_PERSONAL_AGENT_V2_PERSISTENCE_SMOKE=${config.runPersistenceSmoke}`);
  console.log(`EXPECT_PERSONAL_AGENT_FIRESTORE_PERSISTENCE=${config.expectFirestore}`);
  console.log(`FIREBASE_ID_TOKEN set=${Boolean(config.token)}`);
  console.log(`FIREBASE_ID_TOKEN_B set=${Boolean(config.tokenB)}`);

  if (!config.apiBaseUrl) {
    skip("API smoke", "API_BASE_URL is not set.");
    return;
  }

  await step("API /health returns healthy", checkHealth);

  if (!config.token) {
    skip("Authenticated Personal Agent v2 flow", "FIREBASE_ID_TOKEN is not set.");
    return;
  }

  if (!config.runPersistenceSmoke) {
    skip(
      "Personal Agent v2 persistence mutation smoke",
      "Set RUN_PERSONAL_AGENT_V2_PERSISTENCE_SMOKE=true only after preview-only Firestore persistence enablement approval."
    );
    return;
  }

  const initialList = await step("List pending actions and verify persistence metadata", listActions);
  assertPersistenceMetadata(initialList.persistence);

  const pending = await step("Create pending action", createPendingAction);
  await step("List restores created pending action", () => assertListedAction(pending.actionId, "pending"));

  const confirmed = await step("Confirm pending action without execution", () => confirmPendingAction(pending.actionId));
  assertActionView(confirmed, "confirmed");
  await step("List restores confirmed action", () => assertListedAction(pending.actionId, "confirmed"));
  await step("Cancel after confirm returns conflict with confirmed view", () => assertCancelAfterConfirmConflict(pending.actionId));

  const second = await step("Create second pending action", createPendingAction);
  const cancelled = await step("Cancel second pending action", () => cancelPendingAction(second.actionId));
  assertActionView(cancelled, "cancelled");
  await step("List restores cancelled action", () => assertListedAction(second.actionId, "cancelled"));
  await step("Confirm after cancel returns conflict with cancelled view", () => assertConfirmAfterCancelConflict(second.actionId));

  if (config.tokenB) {
    await step("Cross-user confirm returns not_found", () => assertCrossUserBlocked(pending.actionId));
  } else {
    skip("Cross-user owner isolation smoke", "FIREBASE_ID_TOKEN_B is not set.");
  }

  await step("No-write assertion summary", noWriteAssertionSummary);
}

async function checkHealth() {
  const res = await request(`${config.apiBaseUrl}/health`);
  const text = await res.text();
  assert(res.ok, `Expected /health 2xx, got ${res.status}: ${text}`);
  assert(text.includes("healthy"), `Expected health body to contain healthy, got: ${text}`);
}

async function listActions(token = config.token) {
  const res = await request(`${config.apiBaseUrl}/api/agent/pending-actions`, {
    headers: authHeaders(token),
    cache: "no-store"
  });
  const body = await parseJson(res);
  assert(res.ok, `Expected list 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === true, `Expected list success=true: ${JSON.stringify(body)}`);
  assert(Array.isArray(body.data), `Expected list data array: ${JSON.stringify(body)}`);
  assert(body.persistence, `Expected persistence metadata: ${JSON.stringify(body)}`);
  return body;
}

async function createPendingAction() {
  const title = `${config.prefix} Personal Agent v2 persistence ${Date.now()}`;
  const res = await request(`${config.apiBaseUrl}/api/agent/pending-actions`, {
    method: "POST",
    headers: {
      ...authHeaders(config.token),
      "content-type": "application/json"
    },
    body: JSON.stringify({
      title,
      summary: "Verify pending action state persists across list/refresh reads without executing real tools."
    }),
    cache: "no-store"
  });
  const body = await parseJson(res);
  assert(res.ok, `Expected create 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === true, `Expected create success=true: ${JSON.stringify(body)}`);
  assert(body.data?.actionId, `Expected created actionId: ${JSON.stringify(body)}`);
  assertActionView(body.data, "pending");
  return body.data;
}

async function confirmPendingAction(actionId) {
  const { res, body } = await updateAction(actionId, "confirm", config.token);
  assert(res.ok, `Expected confirm 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === true, `Expected confirm success=true: ${JSON.stringify(body)}`);
  assert(body.data, `Expected confirm action view: ${JSON.stringify(body)}`);
  return body.data;
}

async function cancelPendingAction(actionId) {
  const { res, body } = await updateAction(actionId, "cancel", config.token);
  assert(res.ok, `Expected cancel 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === true, `Expected cancel success=true: ${JSON.stringify(body)}`);
  assert(body.data, `Expected cancel action view: ${JSON.stringify(body)}`);
  return body.data;
}

async function assertListedAction(actionId, expectedStatus) {
  const list = await listActions();
  assertPersistenceMetadata(list.persistence);
  const action = list.data.find((item) => item.actionId === actionId);
  assert(action, `Expected action ${actionId} in list: ${JSON.stringify(list.data)}`);
  assertActionView(action, expectedStatus);
  return action;
}

async function assertCancelAfterConfirmConflict(actionId) {
  const { res, body } = await updateAction(actionId, "cancel", config.token);
  assert(res.status === 409, `Expected cancel-after-confirm 409, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === false, `Expected cancel-after-confirm success=false: ${JSON.stringify(body)}`);
  assert(body.status === "confirmed", `Expected conflict status=confirmed: ${JSON.stringify(body)}`);
  assertActionView(body.data, "confirmed");
}

async function assertConfirmAfterCancelConflict(actionId) {
  const { res, body } = await updateAction(actionId, "confirm", config.token);
  assert(res.status === 409, `Expected confirm-after-cancel 409, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === false, `Expected confirm-after-cancel success=false: ${JSON.stringify(body)}`);
  assert(body.status === "cancelled", `Expected conflict status=cancelled: ${JSON.stringify(body)}`);
  assertActionView(body.data, "cancelled");
}

async function assertCrossUserBlocked(actionId) {
  const { res, body } = await updateAction(actionId, "confirm", config.tokenB);
  assert(res.status === 404, `Expected cross-user confirm 404, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === false, `Expected cross-user success=false: ${JSON.stringify(body)}`);
  assert(body.status === "not_found", `Expected cross-user status=not_found: ${JSON.stringify(body)}`);
  assert(!body.data, `Expected cross-user response not to leak action data: ${JSON.stringify(body)}`);
}

async function updateAction(actionId, decision, token) {
  const res = await request(`${config.apiBaseUrl}/api/agent/pending-actions/${actionId}/${decision}`, {
    method: "POST",
    headers: authHeaders(token),
    cache: "no-store"
  });
  return { res, body: await parseJson(res) };
}

function assertPersistenceMetadata(metadata) {
  assert(metadata, "Expected persistence metadata.");
  if (config.expectFirestore) {
    assert(metadata.storeMode === "firestore", `Expected storeMode=firestore: ${JSON.stringify(metadata)}`);
    assert(metadata.firestorePersistenceEnabled === true, `Expected firestorePersistenceEnabled=true: ${JSON.stringify(metadata)}`);
    assert(metadata.previewOnly === true, `Expected previewOnly=true: ${JSON.stringify(metadata)}`);
    assert(
      metadata.safetyMode === "personal_agent_v2_firestore_persistence_preview_only",
      `Expected Firestore preview-only safetyMode: ${JSON.stringify(metadata)}`
    );
    return;
  }

  assert(metadata.firestorePersistenceEnabled === false, `Expected Firestore disabled unless explicitly expected: ${JSON.stringify(metadata)}`);
  assert(
    metadata.safetyMode === "personal_agent_v2_in_memory_preview_only",
    `Expected in-memory preview-only safetyMode: ${JSON.stringify(metadata)}`
  );
}

function assertActionView(action, expectedStatus) {
  assert(action, "Expected action view.");
  assert(action.status === expectedStatus, `Expected status=${expectedStatus}: ${JSON.stringify(action)}`);
  assert(action.executed === false, `Expected executed=false: ${JSON.stringify(action)}`);
  assert(action.wroteData === false, `Expected wroteData=false: ${JSON.stringify(action)}`);
  assert(action.executionReady === false, `Expected executionReady=false: ${JSON.stringify(action)}`);
  assert(action.legacyConfirmEndpointUsed === false, `Expected legacyConfirmEndpointUsed=false: ${JSON.stringify(action)}`);
  assert(action.realWritePath === false, `Expected realWritePath=false: ${JSON.stringify(action)}`);
  assert(action.safetyMode, `Expected safetyMode: ${JSON.stringify(action)}`);
  assert(action.guardDecision === "deny_all_no_real_execution", `Expected deny-all guard decision: ${JSON.stringify(action)}`);
}

async function noWriteAssertionSummary() {
  console.log("No-write assertions:");
  console.log("- no executed=true observed");
  console.log("- no wroteData=true observed");
  console.log("- no realWritePath=true observed");
  console.log("- no legacyConfirmEndpointUsed=true observed");
  console.log("- no life_events or memories API was called by this script");
  console.log("- no external provider or tool execution endpoint was called by this script");
}

async function step(name, fn) {
  try {
    const result = await fn();
    console.log(`PASS ${name}`);
    return result;
  } catch (error) {
    console.error(`FAIL ${name}: ${error.message}`);
    process.exitCode = 1;
    throw error;
  }
}

function skip(name, reason) {
  console.log(`SKIP ${name}: ${reason}`);
}

async function request(url, options = {}) {
  return fetch(url, options);
}

async function parseJson(res) {
  const text = await res.text();
  try {
    return text ? JSON.parse(text) : {};
  } catch {
    throw new Error(`Expected JSON response, got: ${text}`);
  }
}

function authHeaders(token) {
  return {
    Authorization: `Bearer ${token}`
  };
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, "");
}

function isTrue(value) {
  return ["true", "1", "yes"].includes(String(value || "").toLowerCase());
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exitCode = 1;
});

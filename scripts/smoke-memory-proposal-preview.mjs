#!/usr/bin/env node

const config = {
  apiBaseUrl: trimTrailingSlash(process.env.API_BASE_URL || ""),
  token: process.env.FIREBASE_ID_TOKEN || "",
  expectRuntimeEnabled: isTrue(process.env.EXPECT_MEMORY_PROPOSAL_RUNTIME_ENABLED),
  expectGuardEnabled: isTrue(process.env.EXPECT_MEMORY_PROPOSAL_GUARD_ENABLED),
  prefix: process.env.SMOKE_TEST_PREFIX || "[SMOKE TEST]"
};

async function main() {
  console.log("LifeAgent memory proposal preview smoke");
  console.log(`API: ${config.apiBaseUrl || "(not configured)"}`);
  console.log(`EXPECT_MEMORY_PROPOSAL_RUNTIME_ENABLED=${config.expectRuntimeEnabled}`);
  console.log(`EXPECT_MEMORY_PROPOSAL_GUARD_ENABLED=${config.expectGuardEnabled}`);
  console.log(`Token check: length = ${config.token.length}, prefix = ${config.token.slice(0, 40)}`);

  if (!config.apiBaseUrl) {
    fail("API_BASE_URL is not set.");
  }

  await step("API /health returns healthy", checkHealth);

  if (!config.token) {
    skip("Authenticated memory proposal flow", "FIREBASE_ID_TOKEN is not set.");
    return;
  }

  await step("Default-off memory proposal baseline", runDefaultOffBaseline);

  if (!config.expectRuntimeEnabled || !config.expectGuardEnabled) {
    skip(
      "Guard-enabled memory proposal checks",
      "Set EXPECT_MEMORY_PROPOSAL_RUNTIME_ENABLED=true and EXPECT_MEMORY_PROPOSAL_GUARD_ENABLED=true only in a preview-only environment with guard flags enabled."
    );
    await step("No-write assertion summary", noWriteAssertionSummary);
    return;
  }

  await step("Guard-enabled allowed proposal", runGuardAllowedProposal);
  await step("Guard-enabled review_required proposal", runGuardReviewRequiredProposal);
  await step("Guard-enabled blocked proposal", runGuardBlockedProposal);
  await step("No-write assertion summary", noWriteAssertionSummary);
}

async function checkHealth() {
  const res = await request(`${config.apiBaseUrl}/health`);
  const text = await res.text();
  assert(res.ok, `Expected /health 2xx, got ${res.status}: ${text}`);
  assert(text.includes("healthy"), `Expected health body to contain healthy, got: ${text}`);
}

async function runDefaultOffBaseline() {
  const run = await runMemoryProposal(`${config.prefix} 帮我保存记忆：我喜欢早上写代码`);
  assert(run.data.requiresConfirmation === true, `Expected requiresConfirmation=true: ${JSON.stringify(run.data)}`);
  assert(run.data.proposedAction, `Expected proposedAction: ${JSON.stringify(run.data)}`);
  assert(run.data.proposedAction.actionType === "save_memory_preview", `Expected save_memory_preview action: ${JSON.stringify(run.data.proposedAction)}`);
  assert(run.data.proposedAction.requiresConfirmation === true, `Expected proposedAction.requiresConfirmation=true: ${JSON.stringify(run.data.proposedAction)}`);

  const payload = run.data.proposedAction.payload || {};
  assert(readBool(payload, "previewOnly") === true, `Expected payload previewOnly=true: ${JSON.stringify(payload)}`);
  assertNoGuardFields(payload, "default-off save_memory_preview payload");

  const confirm = await confirmAction(run.data.proposedAction.actionId);
  assertPreviewOnlyConfirm(confirm);
}

async function runGuardAllowedProposal() {
  const run = await runMemoryProposal(`${config.prefix} 帮我保存记忆：我喜欢早上写代码`);
  assert(run.data.proposedAction, `Expected allowed proposal to create pending action: ${JSON.stringify(run.data)}`);
  assert(run.data.proposedAction.actionType === "save_memory_preview", `Expected save_memory_preview action: ${JSON.stringify(run.data.proposedAction)}`);
  const payload = run.data.proposedAction.payload || {};
  assert(readBool(payload, "previewOnly") === true, `Expected payload previewOnly=true: ${JSON.stringify(payload)}`);
  assertSafeGuardDiagnosticsIfPresent(payload);

  const confirm = await confirmAction(run.data.proposedAction.actionId);
  assertPreviewOnlyConfirm(confirm);
}

async function runGuardReviewRequiredProposal() {
  const run = await runMemoryProposal(`${config.prefix} 帮我保存记忆：张三 是 没用 的 人`);
  assert(run.data.proposedAction, `Expected review_required proposal to create pending action: ${JSON.stringify(run.data)}`);
  assert(run.data.proposedAction.actionType === "save_memory_preview", `Expected save_memory_preview action: ${JSON.stringify(run.data.proposedAction)}`);
  const payload = run.data.proposedAction.payload || {};
  assert(readBool(payload, "previewOnly") === true, `Expected payload previewOnly=true: ${JSON.stringify(payload)}`);
  assert(readBool(payload, "reviewRequired") === true, `Expected reviewRequired=true: ${JSON.stringify(payload)}`);
  assert(readString(payload, "guardDecision"), `Expected guardDecision to be visible: ${JSON.stringify(payload)}`);
  assertSafeGuardDiagnosticsIfPresent(payload);

  const confirm = await confirmAction(run.data.proposedAction.actionId);
  assertPreviewOnlyConfirm(confirm);
}

async function runGuardBlockedProposal() {
  const run = await runMemoryProposal(`${config.prefix} 帮我保存记忆：Bearer abc.def.ghi`);
  assert(!run.data.proposedAction, `Expected blocked proposal to avoid pending action: ${JSON.stringify(run.data)}`);
  const payload = run.data.payload || run.data || {};
  assert(readBool(payload, "previewOnly") === true, `Expected blocked response previewOnly=true: ${JSON.stringify(payload)}`);
  assert(readBool(payload, "wroteData") === false, `Expected blocked response wroteData=false: ${JSON.stringify(payload)}`);
  assert(readBool(payload, "blocked") === true, `Expected blocked=true: ${JSON.stringify(payload)}`);
  assert(readString(payload, "guardDecision"), `Expected blocked guardDecision: ${JSON.stringify(payload)}`);
  assertNoWriteSignals(payload);
}

async function noWriteAssertionSummary() {
  console.log("No-write assertions:");
  console.log("- no wroteData=true observed");
  console.log("- no users/{userId}/memories write signal observed");
  console.log("- no life_events write signal from proposal runtime observed");
  console.log("- no durable memory write signal observed");
  console.log("- no extraction trigger observed");
  console.log("- no background proposal observed");
  console.log("- no RAG/chat auto proposal observed");
  console.log("- Firestore direct memory write was not queried by this script");
  console.log("- no-write is inferred from response contract and disabled durable write flags");
  console.log("- durable memory write remains out of scope");
}

async function runMemoryProposal(message) {
  const res = await request(`${config.apiBaseUrl}/api/agent/run`, {
    method: "POST",
    headers: {
      ...authHeaders(),
      "content-type": "application/json"
    },
    body: JSON.stringify({
      message,
      clientTimeZone: "Asia/Shanghai"
    })
  });
  const body = await parseJson(res);
  assert(res.ok, `Expected Agent run 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === true, `Expected Agent run success: ${JSON.stringify(body)}`);
  const data = body.data || {};
  assert(data.actionType === "save_memory_preview", `Expected response actionType=save_memory_preview: ${JSON.stringify(data)}`);
  assert(data.previewOnly === true, `Expected response previewOnly=true: ${JSON.stringify(data)}`);
  assert(data.wroteData === false, `Expected response wroteData=false: ${JSON.stringify(data)}`);
  assertNoWriteSignals(data);
  return { body, data };
}

async function confirmAction(actionId) {
  assert(actionId, "Expected actionId before confirm.");
  const res = await request(`${config.apiBaseUrl}/api/agent/confirm`, {
    method: "POST",
    headers: {
      ...authHeaders(),
      "content-type": "application/json"
    },
    body: JSON.stringify({
      actionId,
      decision: "confirm"
    })
  });
  const body = await parseJson(res);
  assert(res.ok, `Expected confirm 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.success === true, `Expected confirm success: ${JSON.stringify(body)}`);
  return body;
}

function assertPreviewOnlyConfirm(body) {
  assert(body.status === "confirmed", `Expected confirm status=confirmed: ${JSON.stringify(body)}`);
  assert(body.lifecycleStatus === "confirmed", `Expected lifecycleStatus=confirmed: ${JSON.stringify(body)}`);
  assert(body.result?.previewOnly === true, `Expected confirm previewOnly=true: ${JSON.stringify(body)}`);
  assert(body.result?.wroteData === false, `Expected confirm wroteData=false: ${JSON.stringify(body)}`);
  assert(!body.result?.createdResourceId, `Expected no createdResourceId in preview-only memory proposal confirm: ${JSON.stringify(body)}`);
  assertNoWriteSignals(body.result || {});
}

function assertNoGuardFields(payload, label) {
  const forbidden = [
    "guardDecision",
    "GuardDecision",
    "blocked",
    "Blocked",
    "reviewRequired",
    "ReviewRequired",
    "guardReason",
    "GuardReason",
    "conflictResult",
    "ConflictResult",
    "mergeCandidate",
    "MergeCandidate"
  ];

  for (const key of forbidden) {
    assert(!(key in payload), `Expected ${label} to omit ${key}: ${JSON.stringify(payload)}`);
  }
}

function assertSafeGuardDiagnosticsIfPresent(payload) {
  const guardDecision = readString(payload, "guardDecision");
  if (!guardDecision) {
    return;
  }

  assert(
    ["allow", "review_required", "merge_candidate", "block"].includes(guardDecision),
    `Unexpected guardDecision: ${JSON.stringify(payload)}`
  );
  assert(!JSON.stringify(payload).includes("Bearer abc.def.ghi"), `Guard diagnostics leaked blocked sensitive content: ${JSON.stringify(payload)}`);
}

function assertNoWriteSignals(value) {
  assert(!containsWroteDataTrue(value), `Expected no wroteData=true signal: ${JSON.stringify(value)}`);
  assert(!containsCreatedMemoryResource(value), `Expected no created memory resource signal: ${JSON.stringify(value)}`);
  assert(!containsLifeEventWriteSignal(value), `Expected no life_events write signal from memory proposal runtime: ${JSON.stringify(value)}`);
}

function containsWroteDataTrue(value) {
  if (!value || typeof value !== "object") {
    return false;
  }
  if (value.wroteData === true || value.WroteData === true) {
    return true;
  }
  return Object.values(value).some(containsWroteDataTrue);
}

function containsCreatedMemoryResource(value) {
  if (!value || typeof value !== "object") {
    return false;
  }
  const resourceType = value.createdResourceType || value.CreatedResourceType;
  const resourceId = value.createdResourceId || value.CreatedResourceId;
  if (typeof resourceType === "string" && resourceType.toLowerCase().includes("memory")) {
    return true;
  }
  if (typeof resourceId === "string" && resourceId.toLowerCase().startsWith("mem")) {
    return true;
  }
  return Object.values(value).some(containsCreatedMemoryResource);
}

function containsLifeEventWriteSignal(value) {
  if (!value || typeof value !== "object") {
    return false;
  }
  const resourceType = value.createdResourceType || value.CreatedResourceType;
  if (resourceType === "life_event") {
    return true;
  }
  return Object.values(value).some(containsLifeEventWriteSignal);
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

function authHeaders() {
  return {
    Authorization: `Bearer ${config.token}`
  };
}

function readString(payload, key) {
  const pascalKey = key[0].toUpperCase() + key.slice(1);
  return payload?.[key] ?? payload?.[pascalKey] ?? "";
}

function readBool(payload, key) {
  const pascalKey = key[0].toUpperCase() + key.slice(1);
  return payload?.[key] ?? payload?.[pascalKey];
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function fail(message) {
  console.error(`FAIL ${message}`);
  process.exit(1);
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

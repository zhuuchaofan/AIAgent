#!/usr/bin/env node

const config = {
  apiBaseUrl: trimTrailingSlash(process.env.API_BASE_URL || ""),
  token: process.env.FIREBASE_ID_TOKEN || "",
  runWriteSmoke: isTrue(process.env.RUN_AGENT_WRITE_SMOKE),
  expectWriteEnabled: isTrue(process.env.EXPECT_AGENT_WRITE_ENABLED),
  prefix: process.env.SMOKE_TEST_PREFIX || "[SMOKE TEST]"
};

async function main() {
  console.log("LifeAgent Agent life_event write smoke");
  console.log(`API: ${config.apiBaseUrl || "(not configured)"}`);
  console.log(`RUN_AGENT_WRITE_SMOKE=${config.runWriteSmoke}`);
  console.log(`EXPECT_AGENT_WRITE_ENABLED=${config.expectWriteEnabled}`);

  if (!config.apiBaseUrl) {
    skip("API smoke", "API_BASE_URL is not set.");
    return;
  }

  await step("API /health returns healthy", checkHealth);

  if (!config.token) {
    skip("Authenticated Agent flow", "FIREBASE_ID_TOKEN is not set.");
    return;
  }

  const run = await step("Agent proposes life_event action", runAgentLifeEventProposal);
  await step("Confirm action and verify expected write mode", () => confirmLifeEvent(run));
  await step("Repeat confirm and verify idempotency", () => repeatConfirm(run));

  if (!config.runWriteSmoke || !config.expectWriteEnabled) {
    skip(
      "Real write assertions",
      "Set RUN_AGENT_WRITE_SMOKE=true and EXPECT_AGENT_WRITE_ENABLED=true to require wroteData=true."
    );
  }
}

async function checkHealth() {
  const res = await request(`${config.apiBaseUrl}/health`);
  const text = await res.text();
  assert(res.ok, `Expected /health 2xx, got ${res.status}: ${text}`);
  assert(text.includes("healthy"), `Expected health body to contain healthy, got: ${text}`);
}

async function runAgentLifeEventProposal() {
  const message = `${config.prefix} 请新增一条 life_event 生活事件记录：今天黑猫吐了一次。type=pet_health，title=黑猫呕吐观察，content=今天黑猫吐了一次，暂时观察精神和食欲。`;
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
  assert(data.requiresConfirmation === true, `Expected requiresConfirmation=true: ${JSON.stringify(data)}`);
  assert(data.proposedAction, `Expected proposedAction: ${JSON.stringify(data)}`);
  assert(data.proposedAction.actionId, `Expected proposedAction.actionId: ${JSON.stringify(data.proposedAction)}`);
  assert(
    ["create_life_event", "create_life_event_preview"].includes(data.proposedAction.actionType),
    `Expected create_life_event or create_life_event_preview action: ${JSON.stringify(data.proposedAction)}`
  );

  return {
    actionId: data.proposedAction.actionId,
    actionType: data.proposedAction.actionType
  };
}

async function confirmLifeEvent(run) {
  const body = await confirmAction(run.actionId);
  assert(body.success === true, `Expected confirm success: ${JSON.stringify(body)}`);
  assert(body.status === "confirmed", `Expected confirm status=confirmed: ${JSON.stringify(body)}`);
  assert(body.lifecycleStatus === "confirmed", `Expected lifecycleStatus=confirmed: ${JSON.stringify(body)}`);

  if (config.runWriteSmoke && config.expectWriteEnabled) {
    assert(body.result?.previewOnly === false, `Expected previewOnly=false: ${JSON.stringify(body)}`);
    assert(body.result?.wroteData === true, `Expected wroteData=true: ${JSON.stringify(body)}`);
    assert(body.result?.createdResourceType === "life_event", `Expected createdResourceType=life_event: ${JSON.stringify(body)}`);
    assert(body.result?.createdResourceId, `Expected createdResourceId: ${JSON.stringify(body)}`);
    assert(
      body.result.createdResourceId === `evt_${run.actionId}`,
      `Expected createdResourceId=evt_${run.actionId}, got ${body.result.createdResourceId}`
    );
    return body;
  }

  assert(body.result?.previewOnly === true, `Expected previewOnly=true when write smoke is not enabled: ${JSON.stringify(body)}`);
  assert(body.result?.wroteData === false, `Expected wroteData=false when write smoke is not enabled: ${JSON.stringify(body)}`);
  assert(!body.result?.createdResourceId, `Expected no createdResourceId in preview-only mode: ${JSON.stringify(body)}`);
  return body;
}

async function repeatConfirm(run) {
  const body = await confirmAction(run.actionId);
  assert(body.success === true, `Expected repeated confirm success: ${JSON.stringify(body)}`);
  assert(body.status === "confirmed", `Expected repeated confirm status=confirmed: ${JSON.stringify(body)}`);
  assert(body.result?.idempotent === true, `Expected idempotent=true on repeated confirm: ${JSON.stringify(body)}`);

  if (config.runWriteSmoke && config.expectWriteEnabled) {
    assert(body.result?.wroteData === true, `Expected repeated wroteData=true: ${JSON.stringify(body)}`);
    assert(body.result?.createdResourceId === `evt_${run.actionId}`, `Expected repeated same createdResourceId: ${JSON.stringify(body)}`);
    return body;
  }

  assert(body.result?.previewOnly === true, `Expected repeated previewOnly=true: ${JSON.stringify(body)}`);
  assert(body.result?.wroteData === false, `Expected repeated wroteData=false: ${JSON.stringify(body)}`);
  return body;
}

async function confirmAction(actionId) {
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
  return body;
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

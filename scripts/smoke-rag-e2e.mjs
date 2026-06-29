#!/usr/bin/env node

import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const config = {
  apiBaseUrl: trimTrailingSlash(process.env.API_BASE_URL || ""),
  webBaseUrl: trimTrailingSlash(process.env.WEB_BASE_URL || "https://life.zhuchaofan.com"),
  token: process.env.FIREBASE_ID_TOKEN || "",
  runMutatingSmoke: isTrue(process.env.RUN_MUTATING_SMOKE),
  ragQuestion: process.env.SMOKE_RAG_QUESTION || "这份测试文档的用途是什么？",
  pollTimeoutMs: Number(process.env.SMOKE_POLL_TIMEOUT_MS || 120000),
  pollIntervalMs: Number(process.env.SMOKE_POLL_INTERVAL_MS || 3000)
};

const state = {
  tempDir: "",
  documentId: "",
  conversationId: `smoke_rag_${Date.now()}`
};

async function main() {
  console.log("LifeAgent RAG smoke test");
  console.log(`API: ${config.apiBaseUrl || "(not configured)"}`);
  console.log(`WEB: ${config.webBaseUrl}`);

  if (!config.apiBaseUrl) {
    throw new Error("API_BASE_URL is required. Get it with: gcloud run services describe life-agent-api --region us-central1 --format='value(status.url)'");
  }

  await step("API /health returns healthy", checkHealth);
  await step("API endpoint responds", checkApiRoot);
  await step("Web endpoint is reachable", checkWebRoot);

  if (!config.token) {
    skip("Authenticated RAG flow", "FIREBASE_ID_TOKEN is not set.");
    return;
  }

  if (!config.runMutatingSmoke) {
    skip("Authenticated upload/RAG/delete flow", "RUN_MUTATING_SMOKE=true is required to create and delete a temporary test document.");
    return;
  }

  try {
    await step("Upload temporary smoke document", uploadDocument);
    await step("Wait for document status success", waitForDocumentSuccess);
    await step("Run RAG question", runRagQuestion);
  } finally {
    await step("Cleanup smoke conversation", cleanupConversation, { bestEffort: true });
    await step("Cleanup smoke document", cleanupDocument, { bestEffort: true });
    if (state.tempDir) {
      await rm(state.tempDir, { recursive: true, force: true });
    }
  }
}

async function checkHealth() {
  const res = await request(`${config.apiBaseUrl}/health`);
  const text = await res.text();
  assert(res.ok, `Expected /health 2xx, got ${res.status}: ${text}`);
  assert(text.includes("healthy"), `Expected health body to contain "healthy", got: ${text}`);
}

async function checkApiRoot() {
  const res = await request(`${config.apiBaseUrl}/`);
  const text = await res.text();
  assert(res.status < 500, `Expected API endpoint to avoid 5xx, got ${res.status}: ${text}`);
}

async function checkWebRoot() {
  const res = await request(`${config.webBaseUrl}/`);
  const text = await res.text();
  assert(res.ok, `Expected Web root 2xx, got ${res.status}: ${text.slice(0, 200)}`);
}

async function uploadDocument() {
  state.tempDir = await mkdtemp(join(tmpdir(), "lifeagent-smoke-"));
  const filePath = join(state.tempDir, "lifeagent-smoke.md");
  await writeFile(filePath, [
    "# LifeAgent Smoke Test",
    "",
    "这是一份自动化验收测试文档。",
    "它的用途是验证上传、处理、RAG 问答和清理链路。"
  ].join("\n"));

  const file = new Blob([await (await import("node:fs/promises")).readFile(filePath)], { type: "text/markdown" });
  const form = new FormData();
  form.append("file", file, "lifeagent-smoke.md");

  const res = await request(`${config.apiBaseUrl}/api/v1/documents/`, {
    method: "POST",
    headers: authHeaders(),
    body: form
  });
  const body = await parseJson(res);
  assert(res.status === 202, `Expected upload 202, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.documentId, `Upload response missing documentId: ${JSON.stringify(body)}`);
  state.documentId = body.documentId;
}

async function waitForDocumentSuccess() {
  const deadline = Date.now() + config.pollTimeoutMs;
  while (Date.now() < deadline) {
    const docs = await listDocuments();
    const doc = docs.find((item) => item.id === state.documentId);
    if (!doc) {
      throw new Error(`Uploaded document ${state.documentId} not found in document list.`);
    }
    if (doc.status === "success") {
      assert(Number(doc.chunkCount || 0) > 0, `Document succeeded but chunkCount is not positive: ${JSON.stringify(doc)}`);
      return;
    }
    if (doc.status === "failed") {
      throw new Error(`Document processing failed: ${doc.errorMessage || "unknown error"}`);
    }
    await sleep(config.pollIntervalMs);
  }
  throw new Error(`Timed out waiting for document ${state.documentId} to reach success.`);
}

async function runRagQuestion() {
  const res = await request(`${config.apiBaseUrl}/api/v1/chat/rag`, {
    method: "POST",
    headers: {
      ...authHeaders(),
      "content-type": "application/json"
    },
    body: JSON.stringify({
      conversationId: state.conversationId,
      message: config.ragQuestion,
      documentIds: [state.documentId],
      clientTimeZone: "Asia/Shanghai"
    })
  });
  const body = await parseJson(res);
  assert(res.ok, `Expected RAG response 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(body.response || body.answer || body.content, `RAG response missing answer/content: ${JSON.stringify(body)}`);

  if (Array.isArray(body.citations) && body.citations.length > 0) {
    const citation = body.citations[0];
    assert(citation.documentId && citation.documentName, `Citation structure is incomplete: ${JSON.stringify(citation)}`);
  } else {
    console.log("RAG response did not include citations; this is allowed for smoke, but should be reviewed if unexpected.");
  }
}

async function cleanupConversation() {
  if (!state.conversationId || !config.token) return;
  const res = await request(`${config.apiBaseUrl}/api/v1/chat/rag/${encodeURIComponent(state.conversationId)}/messages`, {
    method: "DELETE",
    headers: authHeaders()
  });
  if (!res.ok && res.status !== 404) {
    const text = await res.text();
    throw new Error(`Failed to cleanup conversation ${state.conversationId}: ${res.status} ${text}`);
  }
}

async function cleanupDocument() {
  if (!state.documentId || !config.token) return;
  const res = await request(`${config.apiBaseUrl}/api/v1/documents/${encodeURIComponent(state.documentId)}`, {
    method: "DELETE",
    headers: authHeaders()
  });
  if (!res.ok && res.status !== 404) {
    const text = await res.text();
    throw new Error(`Failed to cleanup document ${state.documentId}: ${res.status} ${text}`);
  }
}

async function listDocuments() {
  const res = await request(`${config.apiBaseUrl}/api/v1/documents/`, {
    headers: authHeaders()
  });
  const body = await parseJson(res);
  assert(res.ok, `Expected document list 2xx, got ${res.status}: ${JSON.stringify(body)}`);
  assert(Array.isArray(body.data), `Document list response missing data array: ${JSON.stringify(body)}`);
  return body.data;
}

async function step(name, fn, options = {}) {
  try {
    await fn();
    console.log(`PASS ${name}`);
  } catch (error) {
    if (options.bestEffort) {
      console.error(`WARN ${name}: ${error.message}`);
      return;
    }
    console.error(`FAIL ${name}: ${error.message}`);
    process.exitCode = 1;
    throw error;
  }
}

function skip(name, reason) {
  console.log(`SKIP ${name}: ${reason}`);
}

function authHeaders() {
  return {
    authorization: `Bearer ${config.token}`
  };
}

async function request(url, options = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 30000);
  try {
    return await fetch(url, { ...options, signal: controller.signal });
  } finally {
    clearTimeout(timeout);
  }
}

async function parseJson(res) {
  const text = await res.text();
  try {
    return text ? JSON.parse(text) : {};
  } catch {
    throw new Error(`Expected JSON response, got status ${res.status}: ${text.slice(0, 500)}`);
  }
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function trimTrailingSlash(value) {
  return value.replace(/\/+$/, "");
}

function isTrue(value) {
  return value === "true" || value === "1" || value === "yes";
}

main().catch(() => {
  process.exitCode = 1;
});

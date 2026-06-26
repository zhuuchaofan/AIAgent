"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export async function getDocuments() {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/v1/documents`, {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.message || "Failed to fetch documents");
  }
  return data;
}

export async function uploadDocument(formData: FormData) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/v1/documents`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      // 注意：千万不要手动设置 Content-Type，FormData 会由 fetch 自动配置 boundary
    },
    body: formData,
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.message || "Upload failed");
  }
  return data;
}

export async function deleteDocument(documentId: string) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/v1/documents/${documentId}`, {
    method: "DELETE",
    headers: {
      Authorization: `Bearer ${token}`,
    },
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.message || "Failed to delete document");
  }
  return data;
}

export async function sendRagMessage(
  conversationId: string,
  message: string,
  documentIds?: string[],
  clientTimeZone?: string
) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/v1/chat/rag`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({
      conversationId,
      message,
      documentIds,
      clientTimeZone: clientTimeZone || "Asia/Shanghai",
    }),
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.message || "RAG Chat failed");
  }
  return data;
}

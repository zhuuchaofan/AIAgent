"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface MemoryItem {
  id: string;
  type: string;
  content: string;
  importance: number;
  confidence: number;
  source: string;
  sourceEventIds: string[];
  status: string;
  createdAt: string;
  updatedAt?: string | null;
  expiresAt?: string | null;
}

export async function getMemoryItems(status = "active", type?: string): Promise<MemoryItem[]> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/memory/items`);
  url.searchParams.set("status", status);
  if (type && type !== "all") {
    url.searchParams.set("type", type);
  }

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to fetch memories");
  }

  return data.data ?? [];
}

export async function archiveMemoryItem(memoryId: string): Promise<MemoryItem> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/memory/items/${encodeURIComponent(memoryId)}/archive`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to archive memory");
  }

  return data.data;
}

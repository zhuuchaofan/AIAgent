"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface MemoryInsight {
  kind: "theme" | "preference" | "habit" | "goal" | "temporary_context";
  text: string;
  confidence: number;
  sourceEventIds: string[];
}

export interface MemoryInsightPreviewData {
  scannedCount: number;
  previewOnly: boolean;
  wroteData: boolean;
  memoryWriteEnabled: boolean;
  insights: MemoryInsight[];
}

export async function getMemoryInsightPreview(limit = 20): Promise<MemoryInsightPreviewData> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/memory/insights/preview`);
  url.searchParams.set("limit", String(limit));

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to fetch memory insight preview");
  }

  return data.data;
}

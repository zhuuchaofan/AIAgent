"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface MemoryReviewCandidate {
  id: string;
  type: "theme" | "preference" | "habit" | "goal" | "temporary_context";
  title: string;
  detail: string;
  sourceEventIds: string[];
  sources: MemoryReviewSource[];
  confidence: number;
  reason: string;
  previewOnly: boolean;
  wroteData: boolean;
}

export interface MemoryReviewSource {
  eventId: string;
  title: string;
  snippet: string;
  occurredAt: string;
}

export interface MemoryReviewInboxPreviewData {
  scannedCount: number;
  previewOnly: boolean;
  wroteData: boolean;
  candidates: MemoryReviewCandidate[];
}

export async function getMemoryReviewInboxPreview(limit = 20): Promise<MemoryReviewInboxPreviewData> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/memory/review-inbox/preview`);
  url.searchParams.set("limit", String(limit));

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to fetch memory review inbox preview");
  }

  return data.data;
}

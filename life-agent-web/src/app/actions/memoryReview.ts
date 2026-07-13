"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface MemoryReviewCandidate {
  id: string;
  type: "theme" | "preference" | "habit" | "goal" | "temporary_context";
  title: string;
  detail: string;
  reviewStage: "observing" | "stable";
  reviewStageLabel: string;
  sourceEventIds: string[];
  sources: MemoryReviewSource[];
  confidence: number;
  reason: string;
  reviewStatus?: "pending" | "kept" | "dismissed";
  reviewedAt?: string | null;
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
  memoryWriteEnabled: boolean;
  candidates: MemoryReviewCandidate[];
}

export interface MemoryReviewCandidateActionData {
  previewOnly: boolean;
  memoryWriteEnabled: boolean;
  wroteMemory: boolean;
  wroteReviewState: boolean;
  data: MemoryReviewCandidate;
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

async function updateMemoryReviewCandidate(candidateId: string, action: "keep" | "dismiss"): Promise<MemoryReviewCandidateActionData> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/memory/review-inbox/${encodeURIComponent(candidateId)}/${action}`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to update memory review candidate");
  }

  return data;
}

export async function keepMemoryReviewCandidate(candidateId: string): Promise<MemoryReviewCandidateActionData> {
  return updateMemoryReviewCandidate(candidateId, "keep");
}

export async function dismissMemoryReviewCandidate(candidateId: string): Promise<MemoryReviewCandidateActionData> {
  return updateMemoryReviewCandidate(candidateId, "dismiss");
}

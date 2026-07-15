"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface LifeReviewCard {
  id: string;
  title: string;
  text: string;
  sourceEventIds: string[];
}

export interface LifeReviewSourceEvent {
  id: string;
  title: string;
  content: string;
  occurredAt: string;
}

export interface LifeReviewResponse {
  success: boolean;
  period: LifeReviewPeriod;
  windowLabel: string;
  cards: LifeReviewCard[];
  sourceEvents: LifeReviewSourceEvent[];
  usedEventCount: number;
  usedMemoryCount: number;
  usedPlanSignalCount: number;
  readOnly: boolean;
  wroteData: boolean;
  executed: boolean;
}

export type LifeReviewPeriod = "recent" | "today" | "week";

export interface KeepLifeReviewCardResponse {
  success: boolean;
  previewOnly: boolean;
  memoryWriteEnabled: boolean;
  wroteMemory: boolean;
  wroteReviewState: boolean;
  memoryId?: string | null;
  data: {
    id: string;
    title: string;
    reviewStatus?: "pending" | "kept" | "dismissed" | "remembered";
  };
}

export async function getLifeReview(
  clientTimeZone?: string,
  limit = 30,
  period: LifeReviewPeriod = "recent"
): Promise<LifeReviewResponse> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/life/review`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({
      clientTimeZone: clientTimeZone || "Asia/Shanghai",
      limit,
      period,
    }),
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "暂时无法整理最近回顾");
  }

  return data;
}

export async function keepLifeReviewCard(
  card: LifeReviewCard
): Promise<KeepLifeReviewCardResponse> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/life/review/cards/keep`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({
      cardId: card.id,
      title: card.title,
      text: card.text,
      sourceEventIds: card.sourceEventIds,
    }),
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "暂时不能放入记忆候选");
  }

  return data;
}

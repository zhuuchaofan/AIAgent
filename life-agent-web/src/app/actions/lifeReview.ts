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
  cards: LifeReviewCard[];
  sourceEvents: LifeReviewSourceEvent[];
  usedEventCount: number;
  usedMemoryCount: number;
  readOnly: boolean;
  wroteData: boolean;
  executed: boolean;
}

export async function getLifeReview(clientTimeZone?: string, limit = 30): Promise<LifeReviewResponse> {
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
    }),
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "暂时无法整理最近回顾");
  }

  return data;
}

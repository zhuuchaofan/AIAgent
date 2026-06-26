"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface DailySummaryData {
  id: string;
  date: string;
  timeZone: string;
  eventCount: number;
  summary: string;
  highlights: string[];
  moodLabel: string;
  moodScore: number | null;
  suggestions: string[];
  generatedBy: string;
  agentRunId: string;
  createdAt: string;
  updatedAt: string;
  forceRegenerated: boolean;
}

export async function generateSummary(
  targetDate: string,
  clientTimeZone: string,
  forceRegenerate = false
): Promise<{ success: boolean; cached: boolean; data: DailySummaryData }> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/daily-summaries/generate`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ targetDate, clientTimeZone, forceRegenerate }),
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "生成每日总结失败");
  }
  return data;
}

export async function getSummaryByDate(
  date: string
): Promise<{ success: boolean; data: DailySummaryData } | null> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/daily-summaries/${date}`, {
    headers: { Authorization: `Bearer ${token}` },
    cache: "no-store",
  });

  if (res.status === 404) return null;

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "查询每日总结失败");
  }
  return data;
}

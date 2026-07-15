"use server";

import { getToken } from "./auth";
import type { LifeEvent } from "./events";
import type { MemoryInsight } from "./memoryInsights";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface HomeOverviewReminder {
  id: string;
  title: string;
  description?: string | null;
  dueAt: string;
  timezone: string;
  status: string;
}

export interface HomeOverviewPlanSignal {
  id: string;
  kind: string;
  title: string;
  content: string;
  createdAt: string;
}

export interface HomeOverviewData {
  recentEvents: LifeEvent[];
  hasMoreRecentEvents: boolean;
  insights: MemoryInsight[];
  memoryReviewCandidateCount: number;
  memoryCount: number;
  pendingReminderCount: number;
  pendingReminders: HomeOverviewReminder[];
  latestReminder?: HomeOverviewReminder | null;
  planSignalCount: number;
  planSignals: HomeOverviewPlanSignal[];
  latestPlanSignal?: HomeOverviewPlanSignal | null;
  readOnly: boolean;
  wroteData: boolean;
  executed: boolean;
}

export async function getHomeOverview(limit = 20): Promise<HomeOverviewData> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/home/overview`);
  url.searchParams.set("limit", String(limit));

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to fetch home overview");
  }

  return data.data;
}

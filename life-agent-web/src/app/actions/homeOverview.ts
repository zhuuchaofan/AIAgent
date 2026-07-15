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

export interface HomeOverviewTodayFocus {
  id: string;
  type: "reminder" | "plan" | "insight";
  title: string;
  reason: string;
  href: string;
  basis: "overdue" | "due_today" | "due_soon" | "memory_related" | "recent_pattern";
}

export interface HomeOverviewData {
  recentEvents: LifeEvent[];
  hasMoreRecentEvents: boolean;
  insights: MemoryInsight[];
  memoryReviewCandidateCount: number;
  memoryReviewPendingCandidateCount?: number;
  memoryReviewKeptCandidateCount?: number;
  memoryReviewRememberedCandidateCount?: number;
  memoryCount: number;
  pendingReminderCount: number;
  pendingReminders: HomeOverviewReminder[];
  latestReminder?: HomeOverviewReminder | null;
  planSignalCount: number;
  planSignals: HomeOverviewPlanSignal[];
  latestPlanSignal?: HomeOverviewPlanSignal | null;
  todayFocus?: HomeOverviewTodayFocus[];
  readOnly: boolean;
  wroteData: boolean;
  executed: boolean;
}

export async function getHomeOverview(limit = 20, timeZone = "Asia/Shanghai"): Promise<HomeOverviewData> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/home/overview`);
  url.searchParams.set("limit", String(limit));
  url.searchParams.set("timeZone", timeZone);

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

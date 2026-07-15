"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface PlanSignal {
  id: string;
  kind: "plan" | "reminder_signal" | string;
  sourceActionId: string;
  sourceActionType: string;
  title: string;
  content: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  archivedAt?: string | null;
}

export async function getPlanSignals(status = "active"): Promise<PlanSignal[]> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/plan-signals`);
  url.searchParams.append("status", status);

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || data.message || "Failed to fetch plan signals");
  }

  return data.data || [];
}

export async function archivePlanSignal(id: string) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/plan-signals/${id}/archive`, {
    method: "PATCH",
    headers: {
      Authorization: `Bearer ${token}`,
    },
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || data.message || "Failed to archive plan signal");
  }

  return data;
}

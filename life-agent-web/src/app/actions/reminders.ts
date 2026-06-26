"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export async function getReminders(status = "pending") {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/reminders`);
  url.searchParams.append("status", status);

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to fetch reminders");
  }
  return data;
}

export async function updateReminder(id: string, payload: { status?: string; dueAt?: string }) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/reminders/${id}`, {
    method: "PATCH",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(payload),
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to update reminder");
  }
  return data;
}

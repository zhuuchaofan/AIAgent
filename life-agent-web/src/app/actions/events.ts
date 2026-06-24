"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export async function ingestEvent(text: string, clientTimeZone: string) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/life/ingest`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ text, clientTimeZone }),
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Ingest failed");
  }
  return data;
}

export async function getEvents(cursor?: string) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/life/events`);
  if (cursor) url.searchParams.append("cursor", cursor);

  const res = await fetch(url.toString(), {
    headers: {
      Authorization: `Bearer ${token}`,
    },
    // Prevent Next.js from aggressively caching this list
    cache: "no-store", 
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to fetch events");
  }
  return data;
}

"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface LifeEvent {
  id: string;
  userId?: string;
  createdAt?: string;
  updatedAt?: string;
  isDeleted?: boolean;
  deletedAt?: string;
  type: string;
  schemaVersion?: string;
  title: string;
  content: string;
  occurredAt: string;
  timeZone?: string;
  tags: string[];
  importance: number;
  source?: string;
  structuredData?: Record<string, unknown>;
  extractionConfidence?: number;
  needsReview?: boolean;
  reminderIntentDetected?: boolean;
  reminderParseStatus?: string;
  reminderParseNote?: string;
  createdReminderId?: string;
}

export async function getEvents(cursor?: string, tag?: string) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const url = new URL(`${API_BASE}/api/life/events`);
  if (cursor) url.searchParams.append("cursor", cursor);
  if (tag) url.searchParams.append("tag", tag);

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

export async function updateEvent(id: string, payload: Partial<LifeEvent>) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/life/events/${id}`, {
    method: "PUT",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify(payload),
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to update event");
  }
  return data;
}

export async function deleteEvent(id: string) {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/life/events/${id}`, {
    method: "DELETE",
    headers: {
      Authorization: `Bearer ${token}`,
    },
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "Failed to delete event");
  }
  return data;
}

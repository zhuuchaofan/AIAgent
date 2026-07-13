"use server";

import { getToken } from "./auth";

const API_BASE = process.env.API_BASE_URL || "http://localhost:5140";

export interface LifeChatResponse {
  success: boolean;
  response: string;
  usedEventCount: number;
  usedMemoryCount: number;
  readOnly: boolean;
  wroteData: boolean;
  executed: boolean;
}

export async function askLifeChat(message: string, clientTimeZone?: string): Promise<LifeChatResponse> {
  const token = await getToken();
  if (!token) throw new Error("Unauthorized");

  const res = await fetch(`${API_BASE}/api/life/chat`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({
      message,
      clientTimeZone: clientTimeZone || "Asia/Shanghai",
    }),
    cache: "no-store",
  });

  const data = await res.json();
  if (!res.ok) {
    throw new Error(data.error?.message || "暂时无法回答这个问题");
  }

  return data;
}

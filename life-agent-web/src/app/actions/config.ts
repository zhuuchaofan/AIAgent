"use server";

export async function getFeatureFlags() {
  return {
    agentPreviewEnabled: process.env.NEXT_PUBLIC_ENABLE_AGENT_PREVIEW === "true",
  };
}

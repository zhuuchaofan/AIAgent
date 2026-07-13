import type { LifeEvent } from "@/app/actions/events";

export interface LifeEventDisplayRecord {
  title: string;
  content: string;
}

export interface LifeEventCleanupCandidate {
  event: LifeEvent;
  reasons: string[];
  proposedTitle: string;
  proposedContent: string;
}

const CLEANUP_RULES = [
  {
    reason: "包含“用户输入”前缀",
    pattern: /^用户输入：\s*/u,
    replacement: "",
  },
  {
    reason: "包含 life_events 写入说明",
    pattern: /。?\s*生活记录确认后写入\s*life_events[；;，,、\s\S]*$/u,
    replacement: "",
  },
  {
    reason: "包含 life_events 写入说明",
    pattern: /。?\s*确认后会写入\s*life_events[；;，,、\s\S]*$/u,
    replacement: "",
  },
  {
    reason: "包含提醒或工具执行说明",
    pattern: /。?\s*提醒与工具操作仍不执行[。.]?\s*$/u,
    replacement: "",
  },
] as const;

export function cleanLifeRecordText(value?: string): string {
  if (!value) return "";

  return CLEANUP_RULES.reduce(
    (text, rule) => text.replace(rule.pattern, rule.replacement),
    value
  )
    .replace(/\s+/gu, " ")
    .trim()
    .replace(/[。.\s]+$/u, "");
}

function normalizeComparableText(value: string): string {
  return value.replace(/[，,。.!！?？\s]/gu, "").toLowerCase();
}

export function getLifeEventDisplayRecord(evt: LifeEvent): LifeEventDisplayRecord {
  const title = cleanLifeRecordText(evt.title);
  const content = cleanLifeRecordText(evt.content);
  const displayTitle = title || content || "未命名记录";
  const displayContent = normalizeComparableText(content) === normalizeComparableText(displayTitle)
    ? ""
    : content;

  return { title: displayTitle, content: displayContent };
}

export function getLifeEventCleanupCandidate(evt: LifeEvent): LifeEventCleanupCandidate | null {
  const reasons = new Set<string>();
  const values = [evt.title ?? "", evt.content ?? ""];

  for (const rule of CLEANUP_RULES) {
    if (values.some(value => rule.pattern.test(value))) {
      reasons.add(rule.reason);
    }
  }

  if (reasons.size === 0) {
    return null;
  }

  const proposed = getLifeEventDisplayRecord(evt);

  return {
    event: evt,
    reasons: Array.from(reasons),
    proposedTitle: proposed.title,
    proposedContent: proposed.content,
  };
}

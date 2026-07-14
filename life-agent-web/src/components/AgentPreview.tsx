"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, Check, Loader2, PenLine, Send, X } from "lucide-react";
import {
  cancelUnifiedInboxPendingAction,
  confirmUnifiedInboxPendingAction,
  createUnifiedInboxPendingAction,
  listUnifiedInboxPendingActions,
} from "@/app/actions/knowledge";

interface Phase80PendingAction {
  actionId: string;
  status: string;
  title: string;
  summary: string;
  actionType: string;
  wroteData: boolean;
  message: string;
}

interface Phase80ActionResponse {
  success: boolean;
  status?: string;
  message?: string;
  data?: Phase80PendingAction;
}

interface Phase80ListResponse {
  success: boolean;
  message?: string;
  data?: Phase80PendingAction[];
}

const LIFE_RECORD_PREVIEW = "life_record_preview";
const REMINDER_PREVIEW = "reminder_preview";
const PLAN_PREVIEW = "plan_preview";

function actionTypeLabel(actionType: string): string {
  if (actionType === LIFE_RECORD_PREVIEW) return "生活记录";
  if (actionType === REMINDER_PREVIEW) return "提醒";
  if (actionType === PLAN_PREVIEW) return "计划";
  return "内容";
}

function actionTypeDescription(actionType: string): string {
  if (actionType === LIFE_RECORD_PREVIEW) return "保存后会出现在最近生活记录里。";
  if (actionType === REMINDER_PREVIEW) return "我先帮你把这条提醒线索留住，暂时不会自动提醒。";
  if (actionType === PLAN_PREVIEW) return "我先帮你把这个计划线索留住。";
  return "我会先把它作为一条待保存内容处理。";
}

function cleanActionText(value?: string): string {
  if (!value) return "";

  return value
    .replace(/^用户输入：\s*/u, "")
    .replace(/。?\s*生活记录确认后写入\s*life_events[；;，,、\s\S]*$/u, "")
    .replace(/。?\s*确认后会写入\s*life_events[；;，,、\s\S]*$/u, "")
    .replace(/。?\s*提醒与工具操作仍不执行[。.]?\s*$/u, "")
    .replace(/\s+/gu, " ")
    .trim()
    .replace(/[。.\s]+$/u, "");
}

export function AgentPreview({ onLifeRecordWritten }: { onLifeRecordWritten?: () => void }) {
  const [inputValue, setInputValue] = useState("");
  const [draftAction, setDraftAction] = useState<Phase80PendingAction | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState<"save" | "cancel" | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const isLifeRecord = draftAction?.actionType === LIFE_RECORD_PREVIEW;
  const displayDraft = useMemo(() => {
    if (!draftAction) return null;

    return {
      title: cleanActionText(draftAction.title) || "待保存内容",
      summary: cleanActionText(draftAction.summary),
    };
  }, [draftAction]);

  const showSummary = useMemo(() => {
    if (!displayDraft) return false;
    return displayDraft.summary.trim() !== "" && displayDraft.summary.trim() !== displayDraft.title.trim();
  }, [displayDraft]);

  const loadLatestDraft = useCallback(async () => {
    try {
      const response = await listUnifiedInboxPendingActions() as Phase80ListResponse;
      if (!response.success) return;

      const latestPending = (response.data ?? []).find(action => action.status === "pending") ?? null;
      setDraftAction(latestPending);
    } catch {
      // 首页不把恢复失败当成用户需要处理的事。
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void loadLatestDraft();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [loadLatestDraft]);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    const text = inputValue.trim();
    if (!text || loading) return;

    setLoading(true);
    setError(null);
    setMessage(null);

    try {
      const response = await createUnifiedInboxPendingAction(
        text,
        text,
        Intl.DateTimeFormat().resolvedOptions().timeZone
      ) as Phase80ActionResponse;

      if (!response.success || !response.data) {
        setError(response.message?.includes("401") ? "当前登录状态无法保存，请重新登录后再试。" : (response.message || "暂时无法保存，请稍后再试。"));
        return;
      }

      setInputValue("");
      setDraftAction(response.data);
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setError(errMsg.includes("401") ? "当前登录状态无法保存，请重新登录后再试。" : (errMsg || "暂时无法保存，请稍后再试。"));
    } finally {
      setLoading(false);
    }
  };

  const handleDecision = async (decision: "save" | "cancel") => {
    if (!draftAction || saving) return;

    setSaving(decision);
    setError(null);
    setMessage(null);

    try {
      const response = (decision === "save"
        ? await confirmUnifiedInboxPendingAction(draftAction.actionId)
        : await cancelUnifiedInboxPendingAction(draftAction.actionId)) as Phase80ActionResponse;

      if (!response.success && !response.data) {
        setError(response.message || "处理失败，请稍后再试。");
        return;
      }

      if (decision === "cancel") {
        setDraftAction(null);
        setMessage("已取消。");
        return;
      }

      if (draftAction.actionType === LIFE_RECORD_PREVIEW && response.data?.wroteData) {
        setDraftAction(null);
        setMessage("已保存到生活记录。");
        onLifeRecordWritten?.();
        return;
      }

      if (draftAction.actionType === REMINDER_PREVIEW && response.data?.wroteData) {
        setDraftAction(null);
        setMessage("已保存到提醒事项。");
        onLifeRecordWritten?.();
        return;
      }

      setDraftAction(null);
      setMessage(draftAction.actionType === REMINDER_PREVIEW
        ? "已留下提醒线索。"
        : "已保存这条线索。");
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setError(errMsg || "处理失败，请稍后再试。");
    } finally {
      setSaving(null);
    }
  };

  return (
    <section className="rounded-2xl border border-cyan-500/20 bg-cyan-500/5 p-4 sm:p-5">
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-cyan-500/10 text-cyan-300">
          <PenLine className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <h2 className="text-lg font-semibold text-zinc-100">今天想记录什么？</h2>
          <p className="mt-1 text-sm text-zinc-500">写一句话，我会帮你整理成生活记录。</p>
        </div>
      </div>

      <form onSubmit={handleSubmit} className="mt-4 space-y-3">
        <textarea
          value={inputValue}
          onChange={(event) => setInputValue(event.target.value)}
          disabled={loading}
          rows={3}
          placeholder="比如：今天骑车回来，路上不太热，心率也不高。"
          className="w-full resize-none rounded-2xl border border-zinc-800/80 bg-zinc-950 px-4 py-3 text-base leading-relaxed text-white placeholder-zinc-600 transition-colors focus:border-cyan-500 focus:outline-none disabled:opacity-50"
        />
        <button
          type="submit"
          disabled={loading || !inputValue.trim()}
          className="flex w-full items-center justify-center gap-2 rounded-xl bg-cyan-600 px-4 py-3 text-sm font-semibold text-white transition-colors hover:bg-cyan-700 disabled:opacity-40"
        >
          {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
          保存
        </button>
      </form>

      {draftAction && (
        <div className="mt-4 rounded-2xl border border-zinc-800/80 bg-zinc-950/60 p-4">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-1 text-xs font-medium text-amber-200">
              待保存
            </span>
            <span className="rounded-full border border-cyan-500/30 bg-cyan-500/10 px-2.5 py-1 text-xs font-medium text-cyan-200">
              {actionTypeLabel(draftAction.actionType)}
            </span>
          </div>
          <div className="mt-3 break-words text-base font-medium leading-relaxed text-zinc-100">{displayDraft?.title}</div>
          {showSummary && (
            <div className="mt-2 break-words text-sm leading-relaxed text-zinc-500">{displayDraft?.summary}</div>
          )}
          <div className="mt-3 text-sm leading-relaxed text-zinc-500">{actionTypeDescription(draftAction.actionType)}</div>
          <div className="mt-4 grid grid-cols-1 gap-2 sm:grid-cols-2">
            <button
              type="button"
              onClick={() => handleDecision("save")}
              disabled={!!saving}
              className="flex items-center justify-center gap-2 rounded-xl bg-emerald-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-emerald-700 disabled:opacity-40"
            >
              {saving === "save" ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
              {isLifeRecord ? "保存到生活记录" : "保存"}
            </button>
            <button
              type="button"
              onClick={() => handleDecision("cancel")}
              disabled={!!saving}
              className="flex items-center justify-center gap-2 rounded-xl bg-zinc-800 px-4 py-2.5 text-sm font-semibold text-zinc-200 transition-colors hover:bg-zinc-700 disabled:opacity-40"
            >
              {saving === "cancel" ? <Loader2 className="h-4 w-4 animate-spin" /> : <X className="h-4 w-4" />}
              取消
            </button>
          </div>
        </div>
      )}

      {message && (
        <div className="mt-3 rounded-xl border border-emerald-500/20 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-200">
          {message}
        </div>
      )}

      {error && (
        <div className="mt-3 flex items-start gap-2 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-sm text-red-200">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span className="break-words">{error}</span>
        </div>
      )}
    </section>
  );
}

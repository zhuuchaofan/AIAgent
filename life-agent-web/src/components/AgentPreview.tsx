"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, Bot, Check, ChevronDown, ChevronRight, Loader2, Send, ShieldAlert, Wrench, X } from "lucide-react";
import {
  cancelPhase80PendingAction,
  confirmAgentAction,
  confirmPhase80PendingAction,
  createPhase80PendingAction,
  listPhase80PendingActions,
  runAgentPreview,
} from "@/app/actions/knowledge";
import { Markdown } from "./Markdown";

interface CitationNode {
  index: number;
  documentId: string;
  documentName: string;
  chunkIndex: number;
  pageNumber: number;
  sectionTitle: string | null;
  snippetPreview: string;
}

interface AgentToolCall {
  step: number;
  toolName: string;
  status: string;
  outputSummary?: string | null;
  errorMessage?: string | null;
  durationMs?: number;
}

interface AgentRunData {
  runId: string;
  mode: string;
  answer: string;
  requiresConfirmation?: boolean;
  proposedAction?: AgentProposedAction | null;
  maxSteps: number;
  stepsUsed: number;
  toolCalls?: AgentToolCall[];
  citations?: CitationNode[];
  citationIntegrity?: string | null;
}

interface AgentRunResponse {
  success: boolean;
  message?: string;
  data?: AgentRunData;
}

interface AgentProposedAction {
  actionId: string;
  actionType: string;
  title: string;
  summary: string;
  payload?: unknown;
  riskLevel: string;
  requiresConfirmation: boolean;
  lifecycleStatus?: string;
  createdAt: string;
  expiresAt: string;
}

interface AgentConfirmationResponse {
  success: boolean;
  status: string;
  message: string;
  actionId?: string;
  actionType?: string;
  lifecycleStatus?: string;
  result?: unknown;
}

interface Phase80PendingAction {
  actionId: string;
  status: string;
  title: string;
  summary: string;
  actionType: string;
  createdAt: string;
  expiresAt: string;
  confirmedAt?: string | null;
  cancelledAt?: string | null;
  executed: boolean;
  wroteData: boolean;
  executionReady: boolean;
  guardDecision: string;
  safetyMode: string;
  legacyConfirmEndpointUsed: boolean;
  realWritePath: boolean;
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
  persistence?: Phase80PersistenceMetadata;
}

interface Phase80PersistenceMetadata {
  storeMode: string;
  firestorePersistenceEnabled: boolean;
  previewOnly: boolean;
  safetyMode: string;
}

export function AgentPreview() {
  const [expanded, setExpanded] = useState(true);
  const [inputValue, setInputValue] = useState("");
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState<"confirm" | "cancel" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<AgentRunData | null>(null);
  const [confirmationResult, setConfirmationResult] = useState<AgentConfirmationResponse | null>(null);
  const [phase80Actions, setPhase80Actions] = useState<Phase80PendingAction[]>([]);
  const [phase80Loading, setPhase80Loading] = useState(false);
  const [phase80Refreshing, setPhase80Refreshing] = useState(false);
  const [phase80Updating, setPhase80Updating] = useState<string | null>(null);
  const [phase80Message, setPhase80Message] = useState<string | null>(null);
  const [phase80Persistence, setPhase80Persistence] = useState<Phase80PersistenceMetadata | null>(null);
  const [nowMs, setNowMs] = useState(() => Date.now());

  const toolCalls = useMemo(() => result?.toolCalls ?? [], [result]);
  const citations = useMemo(() => result?.citations ?? [], [result]);
  const actionExpired = result?.proposedAction ? new Date(result.proposedAction.expiresAt).getTime() <= nowMs : false;
  const actionResolved = !!confirmationResult || actionExpired;
  const showPendingAction = !!result?.requiresConfirmation && !!result.proposedAction && !actionResolved;

  const loadPhase80Actions = useCallback(async () => {
    setPhase80Refreshing(true);
    setPhase80Message(null);

    try {
      const res = await listPhase80PendingActions() as Phase80ListResponse;
      if (!res.success) {
        setPhase80Message(res.message || "无法恢复待确认动作。");
        return;
      }

      setPhase80Actions(res.data ?? []);
      setPhase80Persistence(res.persistence ?? null);
      if ((res.data ?? []).length > 0) {
        setPhase80Message("已从 Firestore 恢复待确认动作历史；刷新后状态会保留。");
      }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setPhase80Message(errMsg || "无法恢复待确认动作。");
    } finally {
      setPhase80Refreshing(false);
    }
  }, []);

  useEffect(() => {
    if (!result?.proposedAction || confirmationResult) return;

    const timer = window.setInterval(() => setNowMs(Date.now()), 30_000);
    return () => window.clearInterval(timer);
  }, [result?.proposedAction, confirmationResult]);

  useEffect(() => {
    if (!expanded) return;

    const timer = window.setTimeout(() => {
      void loadPhase80Actions();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [expanded, loadPhase80Actions]);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    const message = inputValue.trim();
    if (!message || loading) return;

    setLoading(true);
    setError(null);
    setConfirmationResult(null);

    try {
      const res = await runAgentPreview(message, "Asia/Shanghai") as AgentRunResponse;
      if (!res.success || !res.data) {
        setResult(null);
        setError(res.message || "个人助手返回异常");
        return;
      }

      setNowMs(Date.now());
      setResult(res.data);
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setResult(null);
      setError(errMsg || "个人助手请求失败");
    } finally {
      setLoading(false);
    }
  };

  const handleConfirmAction = async (decision: "confirm" | "cancel") => {
    if (!result?.proposedAction || confirming || actionResolved) return;

    setConfirming(decision);
    setError(null);
    setConfirmationResult(null);

    try {
      const res = await confirmAgentAction(result.proposedAction.actionId, decision) as AgentConfirmationResponse;
      setConfirmationResult(res);
      if (!res.success) {
        setError(res.message || "Agent 确认失败");
      }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setError(errMsg || "Agent 确认请求失败");
    } finally {
      setConfirming(null);
    }
  };

  const handleCreatePhase80Action = async () => {
    if (phase80Loading) return;

    setPhase80Loading(true);
    setPhase80Message(null);
    setError(null);

    try {
      const res = await createPhase80PendingAction() as Phase80ActionResponse;
      if (!res.success || !res.data) {
        setPhase80Message(res.message || "生成待确认动作失败");
        return;
      }

      setPhase80Actions(prev => [res.data!, ...prev.filter(action => action.actionId !== res.data!.actionId)]);
      setPhase80Message(res.message || "已生成待确认动作");
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setPhase80Message(errMsg || "生成待确认动作失败");
    } finally {
      setPhase80Loading(false);
    }
  };

  const handleUpdatePhase80Action = async (actionId: string, decision: "confirm" | "cancel") => {
    if (phase80Updating) return;

    setPhase80Updating(`${decision}:${actionId}`);
    setPhase80Message(null);

    try {
      const res = (decision === "confirm"
        ? await confirmPhase80PendingAction(actionId)
        : await cancelPhase80PendingAction(actionId)) as Phase80ActionResponse;

      if (!res.data) {
        setPhase80Message(res.message || "待确认动作更新失败");
        return;
      }

      setPhase80Actions(prev => prev.map(action => action.actionId === actionId ? res.data! : action));
      setPhase80Message(res.message || res.data.message);
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setPhase80Message(errMsg || "待确认动作更新失败");
    } finally {
      setPhase80Updating(null);
    }
  };

  const phase80StatusClass = (status: string) => {
    if (status === "confirmed") return "border-emerald-500/30 text-emerald-300 bg-emerald-500/10";
    if (status === "cancelled") return "border-zinc-600/60 text-zinc-300 bg-zinc-800/50";
    if (status === "expired") return "border-red-500/30 text-red-300 bg-red-500/10";
    return "border-amber-500/30 text-amber-200 bg-amber-500/10";
  };

  const phase80StatusLabel = (status: string) => {
    if (status === "confirmed") return "已确认，尚未执行";
    if (status === "cancelled") return "已取消";
    if (status === "expired") return "已过期";
    return "待确认";
  };

  return (
    <section className="border border-zinc-800/50 bg-zinc-900/10 rounded-2xl overflow-hidden">
      <button
        type="button"
        onClick={() => setExpanded(prev => !prev)}
        className="w-full px-5 py-4 flex items-center justify-between gap-3 text-left hover:bg-zinc-900/30 transition-colors"
      >
          <span className="flex items-center gap-2 min-w-0">
          <Bot className="w-4 h-4 text-cyan-400 shrink-0" />
          <span className="text-sm font-semibold text-white">LifeOS 个人助手</span>
          <span className="text-[10px] text-cyan-300 border border-cyan-500/30 bg-cyan-500/10 rounded px-2 py-0.5">Personal Home v1</span>
        </span>
        {expanded ? (
          <ChevronDown className="w-4 h-4 text-zinc-500 shrink-0" />
        ) : (
          <ChevronRight className="w-4 h-4 text-zinc-500 shrink-0" />
        )}
      </button>

      {expanded && (
        <div className="border-t border-zinc-800/50 p-5 space-y-5">
          <form onSubmit={handleSubmit} className="flex flex-col sm:flex-row gap-2">
            <input
              type="text"
              value={inputValue}
              onChange={(event) => setInputValue(event.target.value)}
              disabled={loading}
              placeholder="例如：生成一条待确认动作"
              className="flex-1 bg-zinc-950 border border-zinc-800/80 rounded-xl px-4 py-2.5 text-sm text-white placeholder-zinc-600 focus:outline-none focus:border-cyan-500 transition-colors disabled:opacity-50"
            />
            <button
              type="submit"
              disabled={loading || !inputValue.trim()}
              className="bg-cyan-600 hover:bg-cyan-700 text-white px-4 py-2.5 rounded-xl text-sm font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2 shrink-0"
            >
              {loading ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
              发送
            </button>
          </form>

          {error && (
            <div className="bg-red-500/10 border border-red-500/30 text-red-300 rounded-xl p-3 text-xs flex items-start gap-2">
              <AlertTriangle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
              <span className="break-words [overflow-wrap:anywhere]">{error}</span>
            </div>
          )}

          <div className="bg-zinc-950/50 border border-zinc-800/60 rounded-2xl p-4 text-xs space-y-3">
            <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3">
              <div>
                <div className="text-zinc-200 font-semibold">待确认动作与历史</div>
                <div className="text-zinc-500 mt-1">创建、确认或取消 pending action；状态已持久化，确认仍不会执行真实工具</div>
              </div>
              <div className="flex flex-col sm:flex-row gap-2">
                <button
                  type="button"
                  onClick={loadPhase80Actions}
                  disabled={phase80Refreshing}
                  className="bg-zinc-800 hover:bg-zinc-700 text-zinc-200 px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2 shrink-0"
                >
                  {phase80Refreshing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <ChevronDown className="w-3.5 h-3.5" />}
                  刷新状态
                </button>
                <button
                  type="button"
                  onClick={handleCreatePhase80Action}
                  disabled={phase80Loading}
                  className="bg-amber-600 hover:bg-amber-700 text-white px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2 shrink-0"
                >
                  {phase80Loading ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <ShieldAlert className="w-3.5 h-3.5" />}
                  创建待确认动作
                </button>
              </div>
            </div>

            <div className="rounded-xl border border-cyan-500/20 bg-cyan-500/10 p-3 text-cyan-100 leading-relaxed">
              当前 LifeOS Personal Home v1 已启用 Pending Action 持久化：刷新后可恢复历史状态。确认只代表“用户已确认”，不会写入 memories / life_events，也不会执行真实 tool action。
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-2 text-[10px]">
              <div className="rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-2 text-emerald-100">
                Pending actions persisted
              </div>
              <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2 text-zinc-300">
                Memories write disabled
              </div>
              <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2 text-zinc-300">
                Life events write disabled
              </div>
              <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2 text-zinc-300">
                Real tool execution disabled
              </div>
              <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2 text-zinc-300">
                Legacy confirm path not used
              </div>
            </div>

            {phase80Persistence && (
              <div className="grid grid-cols-1 sm:grid-cols-4 gap-2 text-[10px]">
                <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                  <span className="text-zinc-500">storeMode: </span>
                  <span className="font-mono text-zinc-300">{phase80Persistence.storeMode}</span>
                </div>
                <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                  <span className="text-zinc-500">firestorePersistence: </span>
                  <span className="font-mono text-zinc-300">{String(phase80Persistence.firestorePersistenceEnabled)}</span>
                </div>
                <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                  <span className="text-zinc-500">previewOnly: </span>
                  <span className="font-mono text-zinc-300">{String(phase80Persistence.previewOnly)}</span>
                </div>
                <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                  <span className="text-zinc-500">safetyMode: </span>
                  <span className="font-mono text-zinc-300 break-all">{phase80Persistence.safetyMode}</span>
                </div>
              </div>
            )}

            {phase80Message && (
              <div className="rounded-xl border border-zinc-700/70 bg-zinc-900/60 p-3 text-zinc-300">
                {phase80Message}
              </div>
            )}

            {phase80Actions.length > 0 ? (
              <div className="space-y-2">
                {phase80Actions.map(action => {
                  const pending = action.status === "pending";
                  const confirmKey = `confirm:${action.actionId}`;
                  const cancelKey = `cancel:${action.actionId}`;

                  return (
                    <div key={action.actionId} className="border border-zinc-800/70 rounded-xl p-3 space-y-3">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className={`px-2 py-0.5 rounded border font-mono ${phase80StatusClass(action.status)}`}>
                          {phase80StatusLabel(action.status)}
                        </span>
                        <span className="text-zinc-200 font-medium">{action.title}</span>
                      </div>
                      <div className="text-zinc-400 leading-relaxed">{action.summary}</div>
                      <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-[10px]">
                        <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                          <span className="text-zinc-500">executed: </span>
                          <span className="font-mono text-zinc-300">{String(action.executed)}</span>
                        </div>
                        <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                          <span className="text-zinc-500">wroteData: </span>
                          <span className="font-mono text-zinc-300">{String(action.wroteData)}</span>
                        </div>
                        <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                          <span className="text-zinc-500">guard: </span>
                          <span className="font-mono text-zinc-300 break-all">{action.guardDecision}</span>
                        </div>
                      </div>
                      <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-[10px]">
                        <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                          <span className="text-zinc-500">mode: </span>
                          <span className="font-mono text-zinc-300 break-all">{action.safetyMode}</span>
                        </div>
                        <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                          <span className="text-zinc-500">legacyConfirm: </span>
                          <span className="font-mono text-zinc-300">{String(action.legacyConfirmEndpointUsed)}</span>
                        </div>
                        <div className="rounded-lg border border-zinc-800/70 bg-zinc-950/60 p-2">
                          <span className="text-zinc-500">realWritePath: </span>
                          <span className="font-mono text-zinc-300">{String(action.realWritePath)}</span>
                        </div>
                      </div>
                      <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-amber-100 leading-relaxed">
                        {action.message}
                        {action.status === "confirmed" ? "；已确认，尚未执行。" : ""}
                        <span className="block mt-1">confirmed != executed；当前为 preview-only 模式，不会写入 memories / life_events。</span>
                      </div>
                      {pending ? (
                        <div className="flex flex-col sm:flex-row gap-2">
                          <button
                            type="button"
                            onClick={() => handleUpdatePhase80Action(action.actionId, "confirm")}
                            disabled={!!phase80Updating}
                            className="bg-emerald-600 hover:bg-emerald-700 text-white px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2"
                          >
                            {phase80Updating === confirmKey ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
                            确认但不执行
                          </button>
                          <button
                            type="button"
                            onClick={() => handleUpdatePhase80Action(action.actionId, "cancel")}
                            disabled={!!phase80Updating}
                            className="bg-zinc-800 hover:bg-zinc-700 text-zinc-200 px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2"
                          >
                            {phase80Updating === cancelKey ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <X className="w-3.5 h-3.5" />}
                            取消
                          </button>
                        </div>
                      ) : (
                        <div className="rounded-xl border border-zinc-800/70 bg-zinc-950/60 p-3 text-zinc-400">
                          {action.status === "confirmed"
                            ? "该动作已确认，不能再次确认或取消；它仍未执行。"
                            : action.status === "cancelled"
                              ? "该动作已取消，不能再确认。"
                              : "该动作不再可操作。"}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            ) : (
              <div className="text-zinc-500">还没有待确认动作。生成后可确认或取消，确认后仍不会执行。</div>
            )}
          </div>

          {result && (
            <div className="space-y-4">
              <div className="rounded-2xl border border-zinc-800/60 bg-zinc-950/50 p-4 text-xs text-zinc-400">
                <div className="text-zinc-200 font-semibold mb-1">技术说明：Agent Preview</div>
                <div>下面是旧的只读 Agent / RAG 技术预览结果，用于观察工具调用和引用来源。Personal Home v1 的日常入口是上方的待确认动作与历史。</div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 text-xs">
                <div className="bg-zinc-950/60 border border-zinc-800/60 rounded-xl p-3">
                  <div className="text-zinc-500 mb-1">mode</div>
                  <div className="text-zinc-200 font-mono break-all">{result.mode || "-"}</div>
                </div>
                <div className="bg-zinc-950/60 border border-zinc-800/60 rounded-xl p-3">
                  <div className="text-zinc-500 mb-1">steps</div>
                  <div className="text-zinc-200 font-mono">{result.stepsUsed ?? 0} / {result.maxSteps ?? 0}</div>
                </div>
                <div className="bg-zinc-950/60 border border-zinc-800/60 rounded-xl p-3">
                  <div className="text-zinc-500 mb-1">citationIntegrity</div>
                  <div className="text-zinc-200 font-mono break-all">{result.citationIntegrity || "未返回"}</div>
                </div>
              </div>

              <div className="bg-zinc-950/50 border border-zinc-800/60 rounded-2xl p-4 min-w-0">
                <div className="text-xs font-semibold text-zinc-300 mb-3">回答</div>
                <Markdown content={result.answer || "Agent 未返回回答。"} citations={citations} />
              </div>

              {showPendingAction && result.proposedAction && (
                <div className="bg-amber-500/5 border border-amber-500/30 rounded-2xl p-4 text-xs space-y-3">
                  <div className="flex items-center gap-2 text-amber-200 font-semibold">
                    <ShieldAlert className="w-4 h-4 text-amber-300 shrink-0" />
                    <span>待确认动作</span>
                    <span className="text-[10px] text-amber-300/80 border border-amber-500/30 rounded px-2 py-0.5">Preview</span>
                  </div>
                  <div className="space-y-2 text-zinc-300">
                    <div>
                      <span className="text-zinc-500">动作：</span>
                      <span className="font-mono">{result.proposedAction.actionType}</span>
                    </div>
                    <div>
                      <span className="text-zinc-500">标题：</span>
                      <span>{result.proposedAction.title}</span>
                    </div>
                    <div className="text-zinc-400 leading-relaxed">{result.proposedAction.summary}</div>
                    <div className="text-zinc-500">
                      风险等级：<span className="font-mono text-amber-200">{result.proposedAction.riskLevel}</span>
                    </div>
                    <div className="text-zinc-500">
                      生命周期：<span className="font-mono text-amber-200">{result.proposedAction.lifecycleStatus || "pending"}</span>
                    </div>
                  <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-amber-100 leading-relaxed">
                    这是旧 Agent Preview 确认路径，不属于上方 Personal Home v1 的持久化 pending action 主线。默认不会写入真实业务数据；部署前必须确认真实写入 flag 未开启。
                  </div>
                  </div>
                  <div className="flex flex-col sm:flex-row gap-2">
                    <button
                      type="button"
                      onClick={() => handleConfirmAction("confirm")}
                      disabled={!!confirming || actionResolved}
                      className="bg-emerald-600 hover:bg-emerald-700 text-white px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2"
                    >
                      {confirming === "confirm" ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
                      确认旧预览
                    </button>
                    <button
                      type="button"
                      onClick={() => handleConfirmAction("cancel")}
                      disabled={!!confirming || actionResolved}
                      className="bg-zinc-800 hover:bg-zinc-700 text-zinc-200 px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2"
                    >
                      {confirming === "cancel" ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <X className="w-3.5 h-3.5" />}
                      取消
                    </button>
                  </div>
                </div>
              )}

              {result.requiresConfirmation && result.proposedAction && actionExpired && !confirmationResult && (
                <div className="rounded-2xl border border-zinc-700/60 bg-zinc-950/50 p-4 text-xs text-zinc-400">
                  <div className="font-semibold text-zinc-200 mb-1">待确认动作已过期</div>
                  <div>生命周期：<span className="font-mono">expired</span></div>
                  <div className="mt-2">该 preview action 已失效，不能再确认。请重新发送请求生成新的确认卡片。</div>
                </div>
              )}

              {confirmationResult && (
                <div className={`rounded-2xl border p-4 text-xs ${
                  confirmationResult.success
                    ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-200"
                    : "border-red-500/30 bg-red-500/10 text-red-200"
                }`}>
                  <div className="font-semibold mb-1">确认结果</div>
                  <div className="font-mono mb-1">{confirmationResult.lifecycleStatus || confirmationResult.status}</div>
                  <div>{confirmationResult.message}</div>
                </div>
              )}

              <div className="bg-zinc-950/50 border border-zinc-800/60 rounded-2xl p-4">
                <div className="text-xs font-semibold text-zinc-300 mb-3 flex items-center gap-2">
                  <Wrench className="w-3.5 h-3.5 text-cyan-400" />
                  工具调用
                </div>
                {toolCalls.length > 0 ? (
                  <div className="space-y-2">
                    {toolCalls.map((call) => (
                      <div key={`${call.step}-${call.toolName}`} className="border border-zinc-800/60 rounded-xl p-3 text-xs">
                        <div className="flex flex-wrap items-center gap-2 mb-2">
                          <span className="text-zinc-500">工具：</span>
                          <span className="font-mono text-zinc-200">{call.toolName}</span>
                          <span className={`px-2 py-0.5 rounded border ${
                            call.status === "success"
                              ? "border-emerald-500/30 text-emerald-300 bg-emerald-500/10"
                              : "border-red-500/30 text-red-300 bg-red-500/10"
                          }`}>
                            {call.status}
                          </span>
                        </div>
                        <div className="text-zinc-400 break-words [overflow-wrap:anywhere]">
                          摘要：{call.outputSummary || call.errorMessage || "未返回摘要"}
                        </div>
                        {typeof call.durationMs === "number" && (
                          <div className="text-[10px] text-zinc-600 mt-1">耗时：{call.durationMs} ms</div>
                        )}
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-xs text-zinc-500">本次没有工具调用。</p>
                )}
              </div>

              <div className="bg-zinc-950/50 border border-zinc-800/60 rounded-2xl p-4">
                <div className="text-xs font-semibold text-zinc-300 mb-3">引用来源</div>
                {citations.length > 0 ? (
                  <ul className="space-y-2">
                    {citations.map((citation) => (
                      <li key={`${citation.index}-${citation.documentId}-${citation.chunkIndex}`} className="text-xs text-zinc-400 border border-zinc-800/60 rounded-xl p-3">
                        <div className="text-zinc-200 font-medium break-all">
                          [{citation.index}] {citation.documentName || citation.documentId || "未知文档"}
                        </div>
                        <div className="text-[10px] text-zinc-500 font-mono mt-1">
                          Page {citation.pageNumber || "-"} | Chunk {citation.chunkIndex ?? "-"}
                        </div>
                        <div className="mt-2 leading-relaxed break-words [overflow-wrap:anywhere]">
                          {citation.snippetPreview || "暂无片段预览"}
                        </div>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="text-xs text-zinc-500">
                    本次没有 citations。{result.citationIntegrity ? `citationIntegrity: ${result.citationIntegrity}` : "后端未返回 citationIntegrity。"}
                  </p>
                )}
              </div>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

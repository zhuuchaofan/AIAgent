"use client";

import React, { useMemo, useState } from "react";
import { AlertTriangle, Bot, Check, ChevronDown, ChevronRight, Loader2, Send, ShieldAlert, Wrench, X } from "lucide-react";
import { confirmAgentAction, runAgentPreview } from "@/app/actions/knowledge";
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
  createdAt: string;
  expiresAt: string;
}

interface AgentConfirmationResponse {
  success: boolean;
  status: string;
  message: string;
  actionId?: string;
  actionType?: string;
  result?: unknown;
}

export function AgentPreview() {
  const [expanded, setExpanded] = useState(false);
  const [inputValue, setInputValue] = useState("");
  const [loading, setLoading] = useState(false);
  const [confirming, setConfirming] = useState<"confirm" | "cancel" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [result, setResult] = useState<AgentRunData | null>(null);
  const [confirmationResult, setConfirmationResult] = useState<AgentConfirmationResponse | null>(null);

  const toolCalls = useMemo(() => result?.toolCalls ?? [], [result]);
  const citations = useMemo(() => result?.citations ?? [], [result]);

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
        setError(res.message || "Agent Preview 返回异常");
        return;
      }

      setResult(res.data);
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setResult(null);
      setError(errMsg || "Agent Preview 请求失败");
    } finally {
      setLoading(false);
    }
  };

  const handleConfirmAction = async (decision: "confirm" | "cancel") => {
    if (!result?.proposedAction || confirming) return;

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

  return (
    <section className="border border-zinc-800/50 bg-zinc-900/10 rounded-2xl overflow-hidden">
      <button
        type="button"
        onClick={() => setExpanded(prev => !prev)}
        className="w-full px-5 py-4 flex items-center justify-between gap-3 text-left hover:bg-zinc-900/30 transition-colors"
      >
        <span className="flex items-center gap-2 min-w-0">
          <Bot className="w-4 h-4 text-cyan-400 shrink-0" />
          <span className="text-sm font-semibold text-white">Agent Preview</span>
          <span className="text-[10px] text-zinc-500 border border-zinc-800 rounded px-2 py-0.5">只读实验入口</span>
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
              placeholder="例如：列出我的文档"
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

          {result && (
            <div className="space-y-4">
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

              {result.requiresConfirmation && result.proposedAction && (
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
                    <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-amber-100 leading-relaxed">
                      当前为 preview，不会真正写入生活记录、提醒或记忆。
                    </div>
                  </div>
                  <div className="flex flex-col sm:flex-row gap-2">
                    <button
                      type="button"
                      onClick={() => handleConfirmAction("confirm")}
                      disabled={!!confirming || confirmationResult?.success}
                      className="bg-emerald-600 hover:bg-emerald-700 text-white px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2"
                    >
                      {confirming === "confirm" ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
                      确认
                    </button>
                    <button
                      type="button"
                      onClick={() => handleConfirmAction("cancel")}
                      disabled={!!confirming || confirmationResult?.success}
                      className="bg-zinc-800 hover:bg-zinc-700 text-zinc-200 px-3 py-2 rounded-xl text-xs font-medium transition-colors disabled:opacity-40 flex items-center justify-center gap-2"
                    >
                      {confirming === "cancel" ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <X className="w-3.5 h-3.5" />}
                      取消
                    </button>
                  </div>
                  {confirmationResult && (
                    <div className={`rounded-xl border p-3 ${
                      confirmationResult.success
                        ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-200"
                        : "border-red-500/30 bg-red-500/10 text-red-200"
                    }`}>
                      <div className="font-mono mb-1">{confirmationResult.status}</div>
                      <div>{confirmationResult.message}</div>
                    </div>
                  )}
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

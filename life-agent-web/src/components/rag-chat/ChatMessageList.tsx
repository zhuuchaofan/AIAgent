"use client";

import { RefObject } from "react";
import { AlertTriangle, BookOpen, Loader2, Sparkles } from "lucide-react";
import { Markdown } from "@/components/Markdown";
import type { CitationNode, RagChatMessage } from "./types";

interface ChatMessageListProps {
  messages: RagChatMessage[];
  loading: boolean;
  historyError: string | null;
  chatEndRef: RefObject<HTMLDivElement | null>;
  onRetryHistory: () => void;
}

function CitationList({ citations }: { citations: CitationNode[] }) {
  return (
    <div className="mt-2.5 pt-2.5 border-t border-zinc-800/60 text-xs text-zinc-400 space-y-2 min-w-0">
      <div className="font-semibold text-zinc-300 flex items-center gap-1">
        <BookOpen className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
        <span>引用来源</span>
      </div>
      <ul className="list-none pl-0 space-y-2 max-h-[200px] overflow-y-auto pr-1 min-w-0">
        {citations.map(c => (
          <li key={c.index} className="flex items-start gap-2 hover:text-zinc-200 transition-colors min-w-0">
            <span className="inline-flex items-center justify-center w-4 h-4 text-[9px] font-bold text-indigo-400 bg-indigo-500/10 border border-indigo-500/20 rounded shrink-0 mt-0.5">
              {c.index}
            </span>
            <div className="flex-1 min-w-0">
              <span className="font-medium text-zinc-300 break-all block text-xs" title={c.documentName || c.documentId || "未知来源"}>
                {c.documentName || c.documentId || "未知来源"}
              </span>
              <span className="text-[10px] text-zinc-500 block font-mono">
                Page {c.pageNumber || "-"} | Chunk {c.chunkIndex ?? "-"}
              </span>
              <span className="text-zinc-400 text-[11px] leading-normal italic block mt-0.5 line-clamp-2 break-all">
                &ldquo;{c.snippetPreview || "暂无片段预览"}&rdquo;
              </span>
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function ChatMessageList({
  messages,
  loading,
  historyError,
  chatEndRef,
  onRetryHistory,
}: ChatMessageListProps) {
  return (
    <div className="flex-1 p-5 overflow-y-auto space-y-5 min-h-[380px] max-h-[480px] min-w-0">
      {historyError && (
        <div className="bg-red-500/10 border border-red-500/30 text-red-400 p-4 rounded-xl flex items-center justify-between gap-3 animate-in fade-in duration-300">
          <div className="flex items-center gap-2 text-xs">
            <AlertTriangle className="w-4 h-4 shrink-0 text-red-400" />
            <span>{historyError}</span>
          </div>
          <button
            type="button"
            onClick={onRetryHistory}
            className="bg-red-500/20 hover:bg-red-500/30 text-red-300 text-xs px-2.5 py-1 rounded-lg transition-colors font-medium shrink-0"
          >
            重试加载
          </button>
        </div>
      )}

      {messages.map((msg, index) => (
        <div
          key={index}
          className={`flex gap-3 max-w-[85%] min-w-0 ${
            msg.role === "user" ? "ml-auto flex-row-reverse" : "mr-auto"
          }`}
        >
          <div className={`w-8 h-8 rounded-full shrink-0 flex items-center justify-center font-bold text-xs select-none ${
            msg.role === "user"
              ? "bg-zinc-800 text-zinc-300"
              : "bg-indigo-500/10 text-indigo-400 border border-indigo-500/20"
          }`}>
            {msg.role === "user" ? "U" : "AI"}
          </div>

          <div className="space-y-2 min-w-0 flex-1">
            <div className={`p-3.5 rounded-2xl min-w-0 ${
              msg.role === "user"
                ? "bg-indigo-600 text-white rounded-tr-none shadow-[0_4px_12px_rgba(99,102,241,0.15)]"
                : "bg-zinc-900/60 border border-zinc-800/50 rounded-tl-none"
            }`}>
              {msg.role === "user"
                ? <p className="whitespace-pre-wrap leading-relaxed text-sm text-white break-words [overflow-wrap:anywhere]">{msg.content}</p>
                : <Markdown content={msg.content} citations={msg.citations} />
              }
            </div>

            {msg.role === "assistant" && msg.citations && msg.citations.length > 0 && (
              <CitationList citations={msg.citations} />
            )}

            {msg.role === "assistant" && msg.citationIntegrity === "invalid_cleaned" && (
              <p className="text-[10px] text-amber-500/80 flex items-center gap-1 px-1">
                <AlertTriangle className="w-3.5 h-3.5 text-amber-500 shrink-0" />
                检测到大模型输出发生越界引用，引用的脚标已被安全清洗。
              </p>
            )}
          </div>
        </div>
      ))}

      {messages.length === 1 && !loading && (
        <div className="border border-dashed border-zinc-800 rounded-2xl p-6 text-center text-zinc-500 max-w-md mx-auto my-4 space-y-2">
          <Sparkles className="w-8 h-8 text-zinc-700 mx-auto animate-pulse" />
          <p className="text-xs font-medium text-zinc-400">新建问答会话</p>
          <p className="text-[11px]">当前会话暂无历史问答记录。请在下方输入框中输入您的问题，开启基于个人知识库的深度问答。</p>
        </div>
      )}

      {loading && (
        <div className="flex gap-3 max-w-[80%] mr-auto items-center animate-pulse min-w-0">
          <div className="w-8 h-8 rounded-full bg-indigo-500/10 text-indigo-400 border border-indigo-500/20 flex items-center justify-center text-xs font-bold shrink-0">
            <Loader2 className="w-3.5 h-3.5 animate-spin" />
          </div>
          <div className="bg-zinc-900/40 border border-zinc-800/30 p-3.5 rounded-2xl rounded-tl-none text-xs text-zinc-500 min-w-0">
            正在查找相关文档并整理回答...
          </div>
        </div>
      )}

      <div ref={chatEndRef} />
    </div>
  );
}

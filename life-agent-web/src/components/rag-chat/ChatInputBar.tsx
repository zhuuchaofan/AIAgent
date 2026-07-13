"use client";

import { FormEvent } from "react";
import { Loader2, Send } from "lucide-react";

interface ChatInputBarProps {
  inputValue: string;
  loading: boolean;
  clearing: boolean;
  docsCount: number;
  showClearConfirm: boolean;
  onInputChange: (value: string) => void;
  onSend: (event: FormEvent) => void;
  onCancelClear: () => void;
  onConfirmClear: () => void;
}

export function ChatInputBar({
  inputValue,
  loading,
  clearing,
  docsCount,
  showClearConfirm,
  onInputChange,
  onSend,
  onCancelClear,
  onConfirmClear,
}: ChatInputBarProps) {
  return (
    <>
      {showClearConfirm && (
        <div className="absolute inset-0 flex items-center justify-center bg-zinc-950/80 backdrop-blur-sm rounded-2xl z-10">
          <div className="bg-zinc-900 border border-zinc-700 rounded-2xl p-6 max-w-sm mx-4 shadow-2xl animate-in fade-in zoom-in-95 duration-200">
            <h4 className="text-sm font-semibold text-white mb-2">清除当前对话记录</h4>
            <p className="text-xs text-zinc-400 leading-relaxed mb-5">
              此操作将清除当前知识库问答窗口中的所有历史消息。<br />
              <span className="text-zinc-500">（不会删除已上传的文档和知识库文件）</span>
            </p>
            <div className="flex gap-2 justify-end">
              <button
                type="button"
                onClick={onCancelClear}
                disabled={clearing}
                className="px-4 py-2 text-xs text-zinc-400 hover:text-white bg-zinc-800 hover:bg-zinc-700 rounded-xl transition-colors"
              >
                取消
              </button>
              <button
                type="button"
                onClick={onConfirmClear}
                disabled={clearing}
                className="px-4 py-2 text-xs font-semibold text-white bg-red-600 hover:bg-red-700 rounded-xl transition-colors disabled:opacity-50 flex items-center gap-1.5"
              >
                {clearing ? (
                  <><Loader2 className="w-3 h-3 animate-spin" /> 清除中...</>
                ) : (
                  "确认清除"
                )}
              </button>
            </div>
          </div>
        </div>
      )}

      <form onSubmit={onSend} className="p-4 border-t border-zinc-800/40 bg-zinc-900/20 flex gap-2">
        <input
          type="text"
          value={inputValue}
          onChange={(e) => onInputChange(e.target.value)}
          disabled={loading}
          placeholder={
            docsCount === 0
              ? "请先在知识库管理中上传解析文档..."
              : "输入您关于个人知识库文档的问题，回车发送..."
          }
          className="flex-1 bg-zinc-950 border border-zinc-800/80 rounded-xl px-4 py-2.5 text-sm text-white placeholder-zinc-600 focus:outline-none focus:border-indigo-500 transition-colors disabled:opacity-50"
        />
        <button
          type="submit"
          disabled={loading || !inputValue.trim() || docsCount === 0}
          className="bg-indigo-600 hover:bg-indigo-700 text-white p-2.5 rounded-xl font-medium transition-colors disabled:opacity-30 flex items-center justify-center shrink-0"
          title="发送消息"
        >
          <Send className="w-4 h-4" />
        </button>
      </form>
    </>
  );
}

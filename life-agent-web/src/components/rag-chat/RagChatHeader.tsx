"use client";

import { Sparkles, Trash2 } from "lucide-react";

interface RagChatHeaderProps {
  selectedDocCount: number;
  loading: boolean;
  clearing: boolean;
  onRequestClear: () => void;
}

export function RagChatHeader({
  selectedDocCount,
  loading,
  clearing,
  onRequestClear,
}: RagChatHeaderProps) {
  return (
    <div className="px-5 py-4 border-b border-zinc-800/40 bg-zinc-900/30 flex items-center justify-between">
      <div className="min-w-0">
        <div className="flex items-center gap-2">
          <Sparkles className="w-4 h-4 shrink-0 text-indigo-400" />
          <h3 className="text-sm font-semibold text-white">知识库问答</h3>
        </div>
        <p className="mt-1 text-xs leading-relaxed text-zinc-600">回答主要来自你上传的文档；已记住的个人背景只作辅助参考。</p>
      </div>
      <div className="flex items-center gap-2">
        {selectedDocCount > 0 && (
          <span className="text-[10px] bg-indigo-500/10 text-indigo-400 px-2 py-0.5 rounded font-medium">
            限定 {selectedDocCount} 个文档进行回答
          </span>
        )}
        <button
          type="button"
          onClick={onRequestClear}
          disabled={loading || clearing}
          className="text-zinc-500 hover:text-red-400 p-1.5 rounded-lg hover:bg-red-500/10 transition-colors disabled:opacity-50"
          title="清除当前对话记录"
        >
          <Trash2 className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}

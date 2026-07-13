"use client";

import { BookOpen, FileText, Info } from "lucide-react";
import type { KnowledgeDocument } from "@/providers/DocumentProvider";

interface DocumentFilterPanelProps {
  docs: KnowledgeDocument[];
  selectedDocIds: string[];
  onSelectAll: () => void;
  onToggleDoc: (docId: string) => void;
}

export function DocumentFilterPanel({
  docs,
  selectedDocIds,
  onSelectAll,
  onToggleDoc,
}: DocumentFilterPanelProps) {
  return (
    <div className="lg:col-span-1 bg-zinc-900/20 border border-zinc-800/50 rounded-2xl p-4 flex flex-col space-y-4 shadow-lg">
      <div className="flex items-center gap-2 border-b border-zinc-800/40 pb-3">
        <BookOpen className="w-4 h-4 text-indigo-400" />
        <h3 className="text-sm font-semibold text-white">指定检索文档</h3>
      </div>

      {docs.length === 0 ? (
        <div className="py-8 text-center text-zinc-600 text-xs">
          <Info className="w-8 h-8 text-zinc-800 mx-auto mb-2" />
          <p>暂无解析成功的文档</p>
          <p className="mt-1">请先前往「知识库管理」页面上传文档并等待解析完成。</p>
        </div>
      ) : (
        <div className="flex-1 flex flex-col space-y-3">
          <button
            type="button"
            onClick={onSelectAll}
            className="text-left text-xs font-semibold text-indigo-400 hover:text-indigo-300 transition-colors py-1"
          >
            {selectedDocIds.length === docs.length ? "取消全选" : "全选所有可检索文档"}
          </button>
          <div className="space-y-2 max-h-[350px] overflow-y-auto pr-1">
            {docs.map(doc => (
              <label
                key={doc.id}
                className={`flex items-center gap-2.5 p-2 rounded-xl border text-xs cursor-pointer transition-all ${
                  selectedDocIds.includes(doc.id)
                    ? "bg-indigo-500/5 border-indigo-500/30 text-zinc-200"
                    : "bg-transparent border-zinc-800/40 text-zinc-400 hover:border-zinc-700 hover:text-zinc-300"
                }`}
              >
                <input
                  type="checkbox"
                  checked={selectedDocIds.includes(doc.id)}
                  onChange={() => onToggleDoc(doc.id)}
                  className="accent-indigo-500 rounded border-zinc-700 focus:ring-0 focus:ring-offset-0"
                />
                <FileText className="w-3.5 h-3.5 text-zinc-500 shrink-0" />
                <span className="truncate" title={doc.fileName}>{doc.fileName}</span>
              </label>
            ))}
          </div>
          <div className="pt-2 border-t border-zinc-800/40">
            <p className="text-[10px] text-zinc-500 leading-normal flex items-start gap-1">
              <Info className="w-3 h-3 text-zinc-600 shrink-0 mt-0.5" />
              若不勾选任何文档，后端将自动在整个知识库的所有文档中执行召回检索。
            </p>
          </div>
        </div>
      )}
    </div>
  );
}

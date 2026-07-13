"use client";

import Link from "next/link";
import { Brain } from "lucide-react";

interface MemoryContextNoticeProps {
  memoryCount: number;
}

export function MemoryContextNotice({ memoryCount }: MemoryContextNoticeProps) {
  if (memoryCount <= 0) {
    return null;
  }

  return (
    <div className="border-b border-zinc-800/40 bg-zinc-950/40 px-5 py-3">
      <div className="flex flex-wrap items-center gap-2 text-xs text-zinc-500">
        <Brain className="h-4 w-4 shrink-0 text-indigo-300" />
        <span>
          会把已记住的 {memoryCount} 条个人背景作为辅助参考；引用来源仍只来自知识库文档。
        </span>
        <Link href="/memory" className="text-indigo-300 transition-colors hover:text-indigo-200">
          管理我的记忆
        </Link>
      </div>
    </div>
  );
}

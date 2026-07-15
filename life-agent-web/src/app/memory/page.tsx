"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, Brain, Loader2, MessageCircle, RefreshCw, Trash2 } from "lucide-react";
import { archiveMemoryItem, getMemoryItems, type MemoryItem } from "@/app/actions/memoryItems";
import { formatShortChineseDateTime } from "@/lib/dateFormat";
import { MemoryListSkeleton } from "@/components/LoadingSkeletons";

const typeOptions = [
  { value: "all", label: "全部" },
  { value: "preference", label: "偏好" },
  { value: "habit", label: "习惯" },
  { value: "goal", label: "目标" },
  { value: "theme", label: "主题" },
  { value: "temporary_context", label: "近期背景" },
];

function typeLabel(type: string): string {
  return typeOptions.find(option => option.value === type)?.label ?? "记忆";
}

function buildMemoryQuestion(memory: MemoryItem): string {
  return `结合这条记忆，帮我看看最近状态：${memory.content}`;
}

export default function MemoryPage() {
  const [memories, setMemories] = useState<MemoryItem[]>([]);
  const [activeType, setActiveType] = useState("all");
  const [isLoading, setIsLoading] = useState(true);
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const data = await getMemoryItems("active", activeType);
        if (!cancelled) {
          setMemories(data);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "暂时无法读取记忆");
          setMemories([]);
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    load();

    return () => {
      cancelled = true;
    };
  }, [activeType]);

  const memoryCountText = useMemo(() => {
    if (isLoading) return "正在读取记忆...";
    return memories.length > 0 ? `已记住 ${memories.length} 条` : "还没有记住的事";
  }, [isLoading, memories.length]);

  const archiveMemory = async (memoryId: string) => {
    setUpdatingId(memoryId);
    setError(null);
    try {
      await archiveMemoryItem(memoryId);
      setMemories(prev => prev.filter(memory => memory.id !== memoryId));
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法忘记这条记忆");
    } finally {
      setUpdatingId(null);
    }
  };

  return (
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 selection:bg-indigo-500/30 md:px-10 md:py-10">
      <div className="mx-auto max-w-3xl">
        <header className="mb-8 border-b border-zinc-800/50 pb-6">
          <Link
            href="/"
            className="mb-5 inline-flex items-center gap-2 text-sm text-zinc-500 transition-colors hover:text-zinc-200"
          >
            <ArrowLeft className="h-4 w-4" />
            回到首页
          </Link>
          <div className="flex items-start gap-3">
            <div className="mt-1 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 text-indigo-300">
              <Brain className="h-5 w-5" />
            </div>
            <div>
              <h1 className="text-2xl font-semibold text-zinc-100">我的记忆</h1>
              <p className="mt-2 text-sm leading-relaxed text-zinc-500">
                这些是你明确确认让 LifeOS 记住的事，可以随时忘记。
              </p>
            </div>
          </div>
        </header>

        <div className="mb-5 flex flex-wrap gap-2">
          {typeOptions.map(option => (
            <button
              key={option.value}
              type="button"
              onClick={() => setActiveType(option.value)}
              className={`rounded-full border px-3 py-1.5 text-sm transition-colors ${
                activeType === option.value
                  ? "border-indigo-500/40 bg-indigo-500/10 text-indigo-200"
                  : "border-zinc-800 bg-zinc-900/40 text-zinc-500 hover:border-zinc-700 hover:text-zinc-300"
              }`}
            >
              {option.label}
            </button>
          ))}
        </div>

        <p className="mb-4 text-sm text-zinc-500">{memoryCountText}</p>

        <div className="mb-5 rounded-2xl border border-indigo-500/15 bg-indigo-500/5 p-4">
          <div className="flex items-start gap-3">
            <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-indigo-500/10 text-indigo-300">
              <MessageCircle className="h-4 w-4" />
            </div>
            <div className="min-w-0">
              <p className="text-sm font-medium text-zinc-200">这些记忆会在哪里用到</p>
              <p className="mt-1 text-sm leading-relaxed text-zinc-500">
                生活问答、最近回顾和资料问答会把它们当作你的个人背景参考，但不会把它们当作文档引用，也不会自动执行操作。
              </p>
            </div>
          </div>
        </div>

        {error && (
          <div className="mb-4 rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-sm text-amber-200">
            {error}
          </div>
        )}

        {isLoading ? (
          <MemoryListSkeleton />
        ) : memories.length === 0 ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
            还没有记住的事。你可以先在「可能值得记住的事」里把线索留住，再确认记住。
            <Link href="/memory/review" className="mt-4 inline-flex items-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2 text-sm text-zinc-300 transition-colors hover:border-zinc-700 hover:text-zinc-100">
              去看看候选
            </Link>
          </div>
        ) : (
          <div className="space-y-4">
            {memories.map(memory => (
              <article key={memory.id} className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4">
                <div className="flex items-start gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="mb-2 flex flex-wrap items-center gap-2">
                      <span className="rounded-md border border-indigo-500/20 bg-indigo-500/10 px-2 py-0.5 text-xs text-indigo-300">
                        {typeLabel(memory.type)}
                      </span>
                      <span className="text-xs text-zinc-600">
                        {formatShortChineseDateTime(memory.updatedAt || memory.createdAt)}
                      </span>
                      {memory.sourceEventIds.length > 0 && (
                        <span className="text-xs text-zinc-600">
                          来自 {memory.sourceEventIds.length} 条生活记录
                        </span>
                      )}
                    </div>
                    <p className="break-words text-base font-medium leading-relaxed text-zinc-100">
                      {memory.content}
                    </p>
                    <div className="mt-3 flex flex-wrap gap-2 text-xs text-zinc-500">
                      <span className="inline-flex items-center gap-1 rounded-md border border-zinc-800 bg-zinc-950/40 px-2 py-1">
                        <MessageCircle className="h-3 w-3" />
                        用于生活问答
                      </span>
                      <span className="inline-flex items-center gap-1 rounded-md border border-zinc-800 bg-zinc-950/40 px-2 py-1">
                        <RefreshCw className="h-3 w-3" />
                        用于最近回顾
                      </span>
                      <span className="inline-flex items-center gap-1 rounded-md border border-zinc-800 bg-zinc-950/40 px-2 py-1">
                        <Brain className="h-3 w-3" />
                        用于资料问答背景
                      </span>
                    </div>
                    <div className="mt-4">
                      <Link
                        href={`/life/chat?q=${encodeURIComponent(buildMemoryQuestion(memory))}`}
                        className="inline-flex items-center gap-1.5 rounded-lg border border-indigo-500/20 bg-indigo-500/10 px-3 py-2 text-sm text-indigo-200 transition-colors hover:border-indigo-400/40 hover:bg-indigo-500/15"
                      >
                        <MessageCircle className="h-4 w-4" />
                        围绕这条记忆提问
                      </Link>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => archiveMemory(memory.id)}
                    disabled={updatingId === memory.id}
                    className="shrink-0 rounded-lg border border-zinc-800 bg-zinc-900 p-2 text-zinc-500 transition-colors hover:border-zinc-700 hover:text-zinc-200 disabled:cursor-not-allowed disabled:opacity-50"
                    aria-label="忘记这条记忆"
                    title="忘记这条记忆"
                  >
                    {updatingId === memory.id ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Trash2 className="h-4 w-4" />
                    )}
                  </button>
                </div>
              </article>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}

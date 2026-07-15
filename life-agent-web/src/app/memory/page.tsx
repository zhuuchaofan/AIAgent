"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, Brain, Loader2, MessageCircle, PenLine, RefreshCw, Save, Sparkles, Trash2, X } from "lucide-react";
import { archiveMemoryItem, getMemoryItems, updateMemoryItem, type MemoryItem } from "@/app/actions/memoryItems";
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

const editTypeOptions = [
  { value: "preference", label: "偏好" },
  { value: "habit", label: "习惯" },
  { value: "goal", label: "目标" },
  { value: "theme", label: "主题" },
  { value: "temporary_context", label: "近期背景" },
  { value: "constraint", label: "边界" },
  { value: "routine", label: "惯例" },
  { value: "knowledge", label: "知识" },
  { value: "project", label: "项目" },
  { value: "relationship", label: "关系" },
  { value: "person", label: "人物" },
  { value: "location", label: "地点" },
  { value: "life_event", label: "事实" },
];

type MemoryEditDraft = {
  content: string;
  type: string;
  importance: number;
  expiresAt: string;
};

function typeLabel(type: string): string {
  return editTypeOptions.find(option => option.value === type)?.label ?? "记忆";
}

function buildMemoryQuestion(memory: MemoryItem): string {
  return `结合这条记忆，帮我看看最近状态：${memory.content}`;
}

function memoryUsageText(memory: MemoryItem): string {
  switch (memory.type) {
    case "preference":
      return "我会在建议和回顾里参考这个偏好。";
    case "habit":
      return "我会把它当作你近期习惯的一部分。";
    case "goal":
      return "我会在计划和回顾里留意这个目标。";
    case "temporary_context":
      return "我会把它作为近期背景，过期后不再使用。";
    case "theme":
      return "我会用它帮助整理反复出现的主题。";
    default:
      return "我会把它作为个人背景参考。";
  }
}

function toLocalDateTimeInputValue(value?: string | null): string {
  if (!value) return "";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "";
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
}

function toIsoOrNull(value: string): string | null {
  if (!value.trim()) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function extractFragments(text: string): Set<string> {
  const fragments = new Set<string>();
  const normalized = text.toLowerCase().replace(/[^\p{Script=Han}a-z0-9]+/gu, " ");
  for (const token of normalized.split(/\s+/u).filter(Boolean)) {
    if (/^[a-z0-9]+$/u.test(token)) {
      if (token.length >= 3) fragments.add(token);
      continue;
    }

    for (let index = 0; index < token.length - 1; index += 1) {
      const fragment = token.slice(index, index + 2);
      if (!["一个", "事情", "今天", "最近", "近期", "计划", "目标", "关注", "记录", "记住", "内容"].includes(fragment)) {
        fragments.add(fragment);
      }
    }
  }
  return fragments;
}

function hasLikelyDuplicate(memory: MemoryItem, memories: MemoryItem[]): boolean {
  const left = extractFragments(memory.content);
  if (left.size < 2) return false;

  return memories.some(other => {
    if (other.id === memory.id || other.type !== memory.type) return false;
    const right = extractFragments(other.content);
    let overlap = 0;
    left.forEach(fragment => {
      if (right.has(fragment)) overlap += 1;
    });
    return overlap >= 2;
  });
}

export default function MemoryPage() {
  const [memories, setMemories] = useState<MemoryItem[]>([]);
  const [activeType, setActiveType] = useState("all");
  const [isLoading, setIsLoading] = useState(true);
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editDraft, setEditDraft] = useState<MemoryEditDraft | null>(null);
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

  const startEdit = (memory: MemoryItem) => {
    setError(null);
    setEditingId(memory.id);
    setEditDraft({
      content: memory.content,
      type: memory.type,
      importance: memory.importance,
      expiresAt: toLocalDateTimeInputValue(memory.expiresAt),
    });
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditDraft(null);
  };

  const saveEdit = async (memoryId: string) => {
    if (!editDraft || updatingId) return;

    setUpdatingId(memoryId);
    setError(null);
    try {
      const updated = await updateMemoryItem(memoryId, {
        content: editDraft.content.trim(),
        type: editDraft.type,
        importance: editDraft.importance,
        expiresAt: editDraft.type === "temporary_context" ? toIsoOrNull(editDraft.expiresAt) : null,
      });
      setMemories(prev => prev.map(memory => memory.id === memoryId ? updated : memory));
      cancelEdit();
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法更新这条记忆");
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
              <p className="text-sm font-medium text-zinc-200">记住之后会发生什么</p>
              <p className="mt-1 text-sm leading-relaxed text-zinc-500">
                生活问答和最近回顾会把它们当作你的个人背景参考。它们不会触发提醒、不会执行操作，也可以随时忘记。
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
            {memories.map(memory => {
              const isEditing = editingId === memory.id;
              const duplicate = hasLikelyDuplicate(memory, memories);

              return (
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
                      {duplicate && !isEditing && (
                        <span className="rounded-md border border-amber-500/20 bg-amber-500/10 px-2 py-0.5 text-xs text-amber-200">
                          可能重复
                        </span>
                      )}
                    </div>

                    {isEditing && editDraft ? (
                      <div className="space-y-3">
                        <label className="block text-sm text-zinc-400">
                          记忆内容
                          <textarea
                            value={editDraft.content}
                            onChange={event => setEditDraft(current => current ? { ...current, content: event.target.value } : current)}
                            rows={3}
                            className="mt-1 w-full resize-none rounded-xl border border-zinc-800 bg-zinc-950 px-3 py-2 text-sm leading-relaxed text-zinc-100 outline-none transition-colors focus:border-indigo-500"
                          />
                        </label>
                        <div className="grid gap-3 sm:grid-cols-3">
                          <label className="block text-sm text-zinc-400">
                            类型
                            <select
                              value={editDraft.type}
                              onChange={event => setEditDraft(current => current ? { ...current, type: event.target.value } : current)}
                              className="mt-1 w-full rounded-xl border border-zinc-800 bg-zinc-950 px-3 py-2 text-sm text-zinc-100 outline-none transition-colors focus:border-indigo-500"
                            >
                              {editTypeOptions.map(option => (
                                <option key={option.value} value={option.value}>{option.label}</option>
                              ))}
                            </select>
                          </label>
                          <label className="block text-sm text-zinc-400">
                            重要度
                            <select
                              value={editDraft.importance}
                              onChange={event => setEditDraft(current => current ? { ...current, importance: Number(event.target.value) } : current)}
                              className="mt-1 w-full rounded-xl border border-zinc-800 bg-zinc-950 px-3 py-2 text-sm text-zinc-100 outline-none transition-colors focus:border-indigo-500"
                            >
                              {[1, 2, 3, 4, 5].map(value => (
                                <option key={value} value={value}>{value}</option>
                              ))}
                            </select>
                          </label>
                          {editDraft.type === "temporary_context" && (
                            <label className="block text-sm text-zinc-400">
                              过期时间
                              <input
                                type="datetime-local"
                                value={editDraft.expiresAt}
                                onChange={event => setEditDraft(current => current ? { ...current, expiresAt: event.target.value } : current)}
                                className="mt-1 w-full rounded-xl border border-zinc-800 bg-zinc-950 px-3 py-2 text-sm text-zinc-100 outline-none transition-colors focus:border-indigo-500"
                              />
                            </label>
                          )}
                        </div>
                        <p className="text-xs leading-relaxed text-zinc-600">
                          只会更新这条记忆的内容、类型、重要度和近期背景过期时间；不会改来源、用户归属或创建时间。
                        </p>
                      </div>
                    ) : (
                      <>
                        <p className="break-words text-base font-medium leading-relaxed text-zinc-100">
                          {memory.content}
                        </p>
                        <p className="mt-2 break-words text-sm leading-relaxed text-zinc-500">
                          {memoryUsageText(memory)}
                        </p>
                        {duplicate && (
                          <p className="mt-2 rounded-xl border border-amber-500/15 bg-amber-500/5 px-3 py-2 text-xs leading-relaxed text-amber-100/80">
                            这条记忆和同类型的另一条内容有重合。你可以编辑得更准确，或归档不再需要的一条。
                          </p>
                        )}
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
                            <Sparkles className="h-3 w-3" />
                            用于首页整理
                          </span>
                        </div>
                        <div className="mt-4 flex flex-wrap gap-2">
                          <Link
                            href={`/life/chat?q=${encodeURIComponent(buildMemoryQuestion(memory))}`}
                            className="inline-flex items-center gap-1.5 rounded-lg border border-indigo-500/20 bg-indigo-500/10 px-3 py-2 text-sm text-indigo-200 transition-colors hover:border-indigo-400/40 hover:bg-indigo-500/15"
                          >
                            <MessageCircle className="h-4 w-4" />
                            围绕这条记忆提问
                          </Link>
                          <button
                            type="button"
                            onClick={() => startEdit(memory)}
                            className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2 text-sm text-zinc-300 transition-colors hover:border-zinc-700 hover:text-zinc-100"
                          >
                            <PenLine className="h-4 w-4" />
                            编辑
                          </button>
                        </div>
                      </>
                    )}
                  </div>
                  <div className="flex shrink-0 flex-col gap-2">
                    {isEditing ? (
                      <>
                        <button
                          type="button"
                          onClick={() => saveEdit(memory.id)}
                          disabled={updatingId === memory.id || !editDraft?.content.trim()}
                          className="rounded-lg border border-emerald-500/30 bg-emerald-500/10 p-2 text-emerald-200 transition-colors hover:border-emerald-400/50 disabled:cursor-not-allowed disabled:opacity-50"
                          aria-label="保存修改"
                          title="保存修改"
                        >
                          {updatingId === memory.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                        </button>
                        <button
                          type="button"
                          onClick={cancelEdit}
                          disabled={updatingId === memory.id}
                          className="rounded-lg border border-zinc-800 bg-zinc-900 p-2 text-zinc-500 transition-colors hover:border-zinc-700 hover:text-zinc-200 disabled:cursor-not-allowed disabled:opacity-50"
                          aria-label="取消编辑"
                          title="取消编辑"
                        >
                          <X className="h-4 w-4" />
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        onClick={() => archiveMemory(memory.id)}
                        disabled={updatingId === memory.id}
                        className="rounded-lg border border-zinc-800 bg-zinc-900 p-2 text-zinc-500 transition-colors hover:border-zinc-700 hover:text-zinc-200 disabled:cursor-not-allowed disabled:opacity-50"
                        aria-label="忘记这条记忆"
                        title="忘记这条记忆"
                      >
                        {updatingId === memory.id ? (
                          <Loader2 className="h-4 w-4 animate-spin" />
                        ) : (
                          <Trash2 className="h-4 w-4" />
                        )}
                      </button>
                    )}
                  </div>
                </div>
              </article>
              );
            })}
          </div>
        )}
      </div>
    </main>
  );
}

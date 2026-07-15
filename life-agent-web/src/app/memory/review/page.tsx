"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowLeft, Brain, Check, ChevronDown, Loader2, X } from "lucide-react";
import {
  dismissMemoryReviewCandidate,
  getMemoryReviewInboxPreview,
  keepMemoryReviewCandidate,
  rememberMemoryReviewCandidate,
  type MemoryReviewCandidate
} from "@/app/actions/memoryReview";
import { formatShortChineseDateTime } from "@/lib/dateFormat";
import { MemoryCandidateSkeleton } from "@/components/LoadingSkeletons";

function typeLabel(type: MemoryReviewCandidate["type"]): string {
  switch (type) {
    case "preference":
      return "偏好";
    case "habit":
      return "习惯";
    case "goal":
      return "目标";
    case "temporary_context":
      return "近期背景";
    default:
      return "主题";
  }
}

function stageBadgeClass(stage: MemoryReviewCandidate["reviewStage"]): string {
  switch (stage) {
    case "stable":
      return "border-emerald-500/20 bg-emerald-500/10 text-emerald-300";
    case "one_off":
      return "border-amber-500/20 bg-amber-500/10 text-amber-300";
    default:
      return "border-zinc-700 bg-zinc-900 text-zinc-500";
  }
}

export default function MemoryReviewPage() {
  const [candidates, setCandidates] = useState<MemoryReviewCandidate[]>([]);
  const [activeTab, setActiveTab] = useState<"pending" | "kept" | "remembered">("pending");
  const [expandedSourceIds, setExpandedSourceIds] = useState<Set<string>>(new Set());
  const [draftTexts, setDraftTexts] = useState<Record<string, string>>({});
  const [updatingId, setUpdatingId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const preview = await getMemoryReviewInboxPreview(20);
        if (!cancelled) {
          setCandidates(preview.candidates ?? []);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "暂时无法整理可能值得记住的事");
          setCandidates([]);
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
  }, []);

  const pendingCandidates = candidates.filter(candidate => (candidate.reviewStatus ?? "pending") === "pending");
  const keptCandidates = candidates.filter(candidate => candidate.reviewStatus === "kept");
  const rememberedCandidates = candidates.filter(candidate => candidate.reviewStatus === "remembered");
  const visibleCandidates = activeTab === "pending"
    ? pendingCandidates
    : activeTab === "kept"
      ? keptCandidates
      : rememberedCandidates;

  const toggleSources = (candidateId: string) => {
    setExpandedSourceIds(prev => {
      const next = new Set(prev);
      if (next.has(candidateId)) {
        next.delete(candidateId);
      } else {
        next.add(candidateId);
      }
      return next;
    });
  };

  const keepCandidate = async (candidateId: string) => {
    setUpdatingId(candidateId);
    setActionError(null);
    try {
      const result = await keepMemoryReviewCandidate(candidateId);
      setCandidates(prev => prev.map(candidate => (
        candidate.id === candidateId ? result.data : candidate
      )));
      setDraftTexts(prev => ({
        ...prev,
        [candidateId]: result.data.title
      }));
      setActiveTab("kept");
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "暂时无法保留这条线索");
    } finally {
      setUpdatingId(null);
    }
  };

  const rememberCandidate = async (candidate: MemoryReviewCandidate) => {
    const draft = (draftTexts[candidate.id] ?? candidate.title).trim();
    setUpdatingId(candidate.id);
    setActionError(null);
    try {
      const result = await rememberMemoryReviewCandidate(candidate.id, draft, 3);
      setCandidates(prev => prev.map(item => (
        item.id === candidate.id ? result.data : item
      )));
      setDraftTexts(prev => ({
        ...prev,
        [candidate.id]: result.data.title
      }));
      setActiveTab("remembered");
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "暂时无法记住这条线索");
    } finally {
      setUpdatingId(null);
    }
  };

  const dismissCandidate = async (candidateId: string) => {
    setUpdatingId(candidateId);
    setActionError(null);
    try {
      await dismissMemoryReviewCandidate(candidateId);
      setCandidates(prev => prev.filter(candidate => candidate.id !== candidateId));
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "暂时无法忽略这条线索");
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
              <h1 className="text-2xl font-semibold text-zinc-100">可能值得记住的事</h1>
              <p className="mt-2 text-sm leading-relaxed text-zinc-500">
                我会先把线索放在这里；你可以先留着观察，确认后才会进入「我的记忆」。
              </p>
            </div>
          </div>
        </header>

        <div className="space-y-4">
          <div className="flex rounded-xl border border-zinc-800 bg-zinc-900/30 p-1">
              {[
                { key: "pending" as const, label: `待确认 ${isLoading ? "--" : pendingCandidates.length}` },
                { key: "kept" as const, label: `观察中 ${isLoading ? "--" : keptCandidates.length}` },
                { key: "remembered" as const, label: `已记住 ${isLoading ? "--" : rememberedCandidates.length}` }
              ].map(tab => (
                <button
                  key={tab.key}
                  type="button"
                  onClick={() => setActiveTab(tab.key)}
                  className={`flex-1 rounded-lg px-3 py-2 text-sm transition-colors ${
                    activeTab === tab.key
                      ? "bg-zinc-800 text-zinc-100"
                      : "text-zinc-500 hover:text-zinc-300"
                  }`}
                >
                  {tab.label}
                </button>
              ))}
          </div>

          {actionError && (
            <div className="rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-sm text-amber-200">
              {actionError}
            </div>
          )}

          {!isLoading && !error && (
            <div className="rounded-2xl border border-zinc-800 bg-zinc-900/25 p-4 text-sm leading-relaxed text-zinc-500">
              <span className="font-medium text-zinc-300">这里不是自动记忆。</span>
              {" "}你可以先观察，也可以编辑成更准确的话再确认。只有「确认记住」后，它才会进入我的记忆，并用于之后的生活问答和最近回顾。
            </div>
          )}

          {isLoading ? (
            <MemoryCandidateSkeleton />
          ) : error ? (
            <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
              暂时无法整理这些线索。你可以稍后再来看看。
            </div>
          ) : visibleCandidates.length === 0 ? (
              <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
                {activeTab === "pending"
                  ? "暂时没有需要你判断的线索。继续记录后，我会把更稳定的偏好、习惯和目标放到这里。"
                  : activeTab === "kept"
                    ? "还没有观察中的线索。遇到不确定但有价值的内容，可以先留着，之后再决定是否记住。"
                    : "还没有真正记住的线索。确认记住后，它们会出现在这里和「我的记忆」里。"}
              </div>
          ) : visibleCandidates.map(candidate => (
              <article
                key={candidate.id}
                className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4"
              >
                <div className="flex items-start gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="mb-2 flex flex-wrap items-center gap-2">
                      <span className="rounded-md border border-indigo-500/20 bg-indigo-500/10 px-2 py-0.5 text-xs text-indigo-300">
                        {typeLabel(candidate.type)}
                      </span>
                      <span className={`rounded-md border px-2 py-0.5 text-xs ${stageBadgeClass(candidate.reviewStage)}`}>
                        {candidate.reviewStageLabel || "观察中"}
                      </span>
                      <span className="text-xs text-zinc-600">{candidate.reason}</span>
                      {candidate.reviewStatus === "kept" && (
                        <span className="rounded-md border border-emerald-500/20 bg-emerald-500/10 px-2 py-0.5 text-xs text-emerald-300">
                          先留着
                        </span>
                      )}
                      {candidate.reviewStatus === "remembered" && (
                        <span className="rounded-md border border-sky-500/20 bg-sky-500/10 px-2 py-0.5 text-xs text-sky-300">
                          已记住
                        </span>
                      )}
                    </div>
                    <h2 className="break-words text-base font-semibold text-zinc-100">{candidate.title}</h2>
                    <p className="mt-2 break-words text-sm leading-relaxed text-zinc-500">{candidate.detail}</p>
                    {candidate.sources.length > 0 && (
                      <p className="mt-2 text-xs text-zinc-600">
                        来自最近 {candidate.sources.length} 条生活记录。
                      </p>
                    )}
                    {candidate.reviewStage === "one_off" && (
                      <p className="mt-2 rounded-lg border border-amber-500/10 bg-amber-500/5 px-3 py-2 text-xs leading-relaxed text-amber-200/80">
                        这类线索更像一次发生的事，可以先留着观察，不必急着放进长期记忆。
                      </p>
                    )}
                  </div>
                  <button
                    type="button"
                    onClick={() => dismissCandidate(candidate.id)}
                    disabled={updatingId === candidate.id}
                    className="shrink-0 rounded-lg border border-zinc-800 bg-zinc-900 p-2 text-zinc-500 transition-colors hover:border-zinc-700 hover:text-zinc-200"
                    aria-label="忽略这条线索"
                    title="忽略这条线索"
                  >
                    <X className="h-4 w-4" />
                  </button>
                </div>

                <div className="mt-4 flex flex-wrap gap-2">
                  <button
                    type="button"
                    onClick={() => keepCandidate(candidate.id)}
                    disabled={updatingId === candidate.id || candidate.reviewStatus === "kept" || candidate.reviewStatus === "remembered"}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2 text-sm text-zinc-300 transition-colors hover:border-zinc-700 hover:text-zinc-100"
                  >
                    {updatingId === candidate.id ? (
                      <Loader2 className="h-4 w-4 animate-spin" />
                    ) : (
                      <Check className="h-4 w-4" />
                    )}
                    {candidate.reviewStatus === "remembered" ? "已记住" : candidate.reviewStatus === "kept" ? "观察中" : "先留着"}
                  </button>
                  {(candidate.sources?.length ?? 0) > 0 && (
                    <button
                      type="button"
                      onClick={() => toggleSources(candidate.id)}
                      className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2 text-sm text-zinc-300 transition-colors hover:border-zinc-700 hover:text-zinc-100"
                      aria-expanded={expandedSourceIds.has(candidate.id)}
                    >
                      <ChevronDown className={`h-4 w-4 transition-transform ${expandedSourceIds.has(candidate.id) ? "rotate-180" : ""}`} />
                      查看来源
                    </button>
                  )}
                </div>

                {activeTab === "kept" && (
                  <div className="mt-4 rounded-xl border border-zinc-800 bg-zinc-950/50 p-3">
                    <label htmlFor={`memory-draft-${candidate.id}`} className="text-sm font-medium text-zinc-300">
                      确认前，可以改成你真正想让 LifeOS 记住的话。
                    </label>
                    <p className="mt-1 text-xs leading-relaxed text-zinc-600">
                      观察中不会进入长期记忆；点击确认记住后，才会用于之后的回答和回顾。
                    </p>
                    <textarea
                      id={`memory-draft-${candidate.id}`}
                      value={draftTexts[candidate.id] ?? candidate.title}
                      onChange={event => setDraftTexts(prev => ({
                        ...prev,
                        [candidate.id]: event.target.value
                      }))}
                      disabled={candidate.reviewStatus === "remembered" || updatingId === candidate.id}
                      rows={3}
                      maxLength={500}
                      className="mt-2 min-h-24 w-full resize-y rounded-lg border border-zinc-800 bg-zinc-950 px-3 py-2 text-sm leading-relaxed text-zinc-100 outline-none transition-colors placeholder:text-zinc-600 focus:border-indigo-500/60 disabled:cursor-not-allowed disabled:text-zinc-500"
                    />
                    <div className="mt-3 flex flex-wrap items-center gap-2">
                      <button
                        type="button"
                        onClick={() => rememberCandidate(candidate)}
                        disabled={
                          updatingId === candidate.id ||
                          candidate.reviewStatus === "remembered" ||
                          !(draftTexts[candidate.id] ?? candidate.title).trim()
                        }
                        className="inline-flex items-center gap-1.5 rounded-lg border border-emerald-500/20 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-200 transition-colors hover:border-emerald-400/40 hover:bg-emerald-500/15 disabled:cursor-not-allowed disabled:opacity-50"
                      >
                        {updatingId === candidate.id ? (
                          <Loader2 className="h-4 w-4 animate-spin" />
                        ) : (
                          <Check className="h-4 w-4" />
                        )}
                        确认记住
                      </button>
                      <span className="text-xs text-zinc-600">
                        记住后会出现在「我的记忆」里，并在生活问答和最近回顾中作为背景参考。
                      </span>
                    </div>
                  </div>
                )}

                {activeTab === "remembered" && (
                  <div className="mt-4 rounded-xl border border-sky-500/15 bg-sky-500/5 p-3">
                    <p className="text-sm leading-relaxed text-sky-100">
                      已记住。之后回答和整理时，LifeOS 会把它作为你的个人背景参考。
                    </p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      <Link
                        href="/memory"
                        className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2 text-sm text-zinc-300 transition-colors hover:border-zinc-700 hover:text-zinc-100"
                      >
                        查看我的记忆
                      </Link>
                      <Link
                        href={`/life/chat?q=${encodeURIComponent(`结合这条记忆，帮我看看最近状态：${candidate.title}`)}`}
                        className="inline-flex items-center gap-1.5 rounded-lg border border-indigo-500/20 bg-indigo-500/10 px-3 py-2 text-sm text-indigo-200 transition-colors hover:border-indigo-400/40"
                      >
                        围绕它提问
                      </Link>
                    </div>
                  </div>
                )}

                {expandedSourceIds.has(candidate.id) && (candidate.sources?.length ?? 0) > 0 && (
                  <div className="mt-4 space-y-2 rounded-xl border border-zinc-800 bg-zinc-950/50 p-3">
                    {candidate.sources.map(source => (
                      <div key={source.eventId} className="border-b border-zinc-800/70 pb-2 last:border-b-0 last:pb-0">
                        <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                          <span className="break-words text-sm font-medium text-zinc-200">{source.title}</span>
                          <span className="text-xs text-zinc-600">{formatShortChineseDateTime(source.occurredAt)}</span>
                        </div>
                        {source.snippet && (
                          <p className="mt-1 break-words text-sm leading-relaxed text-zinc-500">{source.snippet}</p>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </article>
          ))}
          <p className="px-1 text-xs leading-relaxed text-zinc-600">
            待确认和观察中只是候选状态；只有点击“确认记住”后，才会进入「我的记忆」。
          </p>
        </div>
      </div>
    </main>
  );
}

"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowLeft, Brain, Check, ChevronDown, Loader2, X } from "lucide-react";
import { getMemoryReviewInboxPreview, type MemoryReviewCandidate } from "@/app/actions/memoryReview";
import { formatShortChineseDateTime } from "@/lib/dateFormat";

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

export default function MemoryReviewPage() {
  const [candidates, setCandidates] = useState<MemoryReviewCandidate[]>([]);
  const [hiddenIds, setHiddenIds] = useState<Set<string>>(new Set());
  const [keptIds, setKeptIds] = useState<Set<string>>(new Set());
  const [expandedSourceIds, setExpandedSourceIds] = useState<Set<string>>(new Set());
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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

  const visibleCandidates = candidates.filter(candidate => !hiddenIds.has(candidate.id));

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
                我会先把线索放在这里，真正记住之前仍需要你确认。
              </p>
            </div>
          </div>
        </header>

        {isLoading ? (
          <div className="flex items-center justify-center rounded-2xl border border-zinc-800 bg-zinc-900/30 py-14 text-zinc-500">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" />
            正在整理...
          </div>
        ) : error ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
            暂时无法整理这些线索。你可以稍后再来看看。
          </div>
        ) : visibleCandidates.length === 0 ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
            暂时没有明显值得记住的线索。继续记录后，我会把更稳定的偏好、习惯和目标放到这里。
          </div>
        ) : (
          <div className="space-y-4">
            {visibleCandidates.map(candidate => (
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
                      <span className={`rounded-md border px-2 py-0.5 text-xs ${
                        candidate.reviewStage === "stable"
                          ? "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"
                          : "border-zinc-700 bg-zinc-900 text-zinc-500"
                      }`}>
                        {candidate.reviewStageLabel || "观察中"}
                      </span>
                      <span className="text-xs text-zinc-600">{candidate.reason}</span>
                      {keptIds.has(candidate.id) && (
                        <span className="rounded-md border border-emerald-500/20 bg-emerald-500/10 px-2 py-0.5 text-xs text-emerald-300">
                          已留在这里
                        </span>
                      )}
                    </div>
                    <h2 className="break-words text-base font-semibold text-zinc-100">{candidate.title}</h2>
                    <p className="mt-2 break-words text-sm leading-relaxed text-zinc-500">{candidate.detail}</p>
                  </div>
                  <button
                    type="button"
                    onClick={() => setHiddenIds(prev => new Set(prev).add(candidate.id))}
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
                    onClick={() => setKeptIds(prev => new Set(prev).add(candidate.id))}
                    className="inline-flex items-center gap-1.5 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2 text-sm text-zinc-300 transition-colors hover:border-zinc-700 hover:text-zinc-100"
                  >
                    <Check className="h-4 w-4" />
                    先留着
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
              这些只是线索，确认记住功能还未开启；忽略和先留着只影响当前页面显示。
            </p>
          </div>
        )}
      </div>
    </main>
  );
}

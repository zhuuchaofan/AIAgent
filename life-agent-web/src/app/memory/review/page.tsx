"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowLeft, Brain, Loader2, X } from "lucide-react";
import { getMemoryReviewInboxPreview, type MemoryReviewCandidate } from "@/app/actions/memoryReview";

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
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="mb-2 flex flex-wrap items-center gap-2">
                      <span className="rounded-md border border-indigo-500/20 bg-indigo-500/10 px-2 py-0.5 text-xs text-indigo-300">
                        {typeLabel(candidate.type)}
                      </span>
                      <span className="text-xs text-zinc-600">{candidate.reason}</span>
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
              </article>
            ))}
          </div>
        )}
      </div>
    </main>
  );
}

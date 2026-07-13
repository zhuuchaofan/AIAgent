"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import {
  ArrowLeft,
  CalendarDays,
  ChevronDown,
  ChevronRight,
  Loader2,
  Sparkles,
} from "lucide-react";
import {
  getLifeReview,
  keepLifeReviewCard,
  type LifeReviewCard,
  type LifeReviewPeriod,
  type LifeReviewSourceEvent,
} from "@/app/actions/lifeReview";
import { useAuth } from "@/providers/AuthProvider";
import { formatShortChineseDateTime } from "@/lib/dateFormat";

export default function LifeReviewPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [cards, setCards] = useState<LifeReviewCard[]>([]);
  const [sourceEvents, setSourceEvents] = useState<LifeReviewSourceEvent[]>([]);
  const [expandedCards, setExpandedCards] = useState<Record<string, boolean>>({});
  const [activePeriod, setActivePeriod] = useState<LifeReviewPeriod>("recent");
  const [keptCardIds, setKeptCardIds] = useState<Record<string, boolean>>({});
  const [keepingCardId, setKeepingCardId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  useEffect(() => {
    if (!user) return;

    let cancelled = false;

    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const review = await getLifeReview(Intl.DateTimeFormat().resolvedOptions().timeZone, 30, activePeriod);

        if (!cancelled) {
          setCards(review.cards ?? []);
          setSourceEvents(review.sourceEvents ?? []);
          setExpandedCards({});
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "暂时无法整理最近回顾");
          setCards([]);
          setSourceEvents([]);
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
  }, [user, activePeriod]);

  const sourceEventMap = useMemo(() => {
    return new Map(sourceEvents.map(event => [event.id, event]));
  }, [sourceEvents]);

  const recentEvents = sourceEvents.slice(0, 5);

  const toggleExpanded = (cardId: string) => {
    setExpandedCards(current => ({
      ...current,
      [cardId]: !current[cardId],
    }));
  };

  const keepCard = async (card: LifeReviewCard) => {
    if (card.sourceEventIds.length === 0) return;

    setKeepingCardId(card.id);
    setActionMessage(null);
    setActionError(null);
    try {
      await keepLifeReviewCard(card);
      setKeptCardIds(current => ({
        ...current,
        [card.id]: true,
      }));
      setActionMessage("已放到可能值得记住的事里。");
    } catch (err) {
      setActionError(err instanceof Error ? err.message : "暂时不能放入记忆候选");
    } finally {
      setKeepingCardId(null);
    }
  };

  const periodOptions: Array<{ key: LifeReviewPeriod; label: string }> = [
    { key: "recent", label: "最近" },
    { key: "today", label: "今天" },
    { key: "week", label: "本周" },
  ];

  if (loading) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-zinc-950">
        <Loader2 className="h-8 w-8 animate-spin text-zinc-500" />
      </main>
    );
  }

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
              <CalendarDays className="h-5 w-5" />
            </div>
            <div>
              <h1 className="text-2xl font-semibold text-zinc-100">最近回顾</h1>
              <p className="mt-2 text-sm leading-relaxed text-zinc-500">
                我会把最近记录里的变化先整理出来，方便你回头看看。
              </p>
            </div>
          </div>
          {user && (
            <div className="mt-6 grid grid-cols-3 rounded-2xl border border-zinc-800 bg-zinc-950/30 p-1">
              {periodOptions.map(option => (
                <button
                  key={option.key}
                  type="button"
                  onClick={() => setActivePeriod(option.key)}
                  className={`rounded-xl px-3 py-2 text-sm transition-colors ${
                    activePeriod === option.key
                      ? "bg-zinc-800 text-zinc-100"
                      : "text-zinc-500 hover:text-zinc-200"
                  }`}
                >
                  {option.label}
                </button>
              ))}
            </div>
          )}
        </header>

        {!user ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6 text-center">
            <p className="text-sm text-zinc-500">请先登录，再查看最近回顾。</p>
            <button
              onClick={loginWithGoogle}
              className="mt-5 rounded-xl bg-white px-5 py-2.5 text-sm font-medium text-zinc-900 hover:bg-zinc-100"
            >
              使用 Google 登录
            </button>
          </div>
        ) : isLoading ? (
          <div className="flex items-center justify-center rounded-2xl border border-zinc-800 bg-zinc-900/30 py-14 text-zinc-500">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" />
            正在整理最近回顾...
          </div>
        ) : error ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
            暂时无法整理最近回顾。你可以稍后再来看看。
          </div>
        ) : (
          <div className="space-y-5">
            {(actionMessage || actionError) && (
              <div className={`rounded-2xl border p-4 text-sm ${
                actionError
                  ? "border-red-500/20 bg-red-500/5 text-red-200"
                  : "border-indigo-500/20 bg-indigo-500/5 text-indigo-200"
              }`}>
                {actionError || actionMessage}
                {actionMessage && (
                  <Link href="/memory/review" className="ml-2 underline underline-offset-4">
                    去查看
                  </Link>
                )}
              </div>
            )}
            {cards.map(item => {
              const sources = item.sourceEventIds
                .map(id => sourceEventMap.get(id))
                .filter((event): event is LifeReviewSourceEvent => Boolean(event));
              const isExpanded = Boolean(expandedCards[item.id]);
              const canKeep = sources.length > 0;
              const isKept = Boolean(keptCardIds[item.id]);
              const isKeeping = keepingCardId === item.id;

              return (
                <section key={item.id} className="rounded-2xl border border-zinc-800 bg-zinc-900/35 p-5">
                  <div className="mb-3 flex items-center gap-2">
                    <Sparkles className="h-4 w-4 text-indigo-300" />
                    <h2 className="text-base font-semibold text-zinc-100">{item.title}</h2>
                  </div>
                  <p className="break-words text-sm leading-relaxed text-zinc-400">
                    {item.text}
                  </p>
                  {canKeep && (
                    <div className="mt-4 flex flex-wrap gap-2">
                      <button
                        type="button"
                        onClick={() => toggleExpanded(item.id)}
                        className="inline-flex items-center gap-1 rounded-lg border border-zinc-800 px-3 py-2 text-xs text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
                        aria-expanded={isExpanded}
                      >
                        {isExpanded ? (
                          <ChevronDown className="h-3.5 w-3.5" />
                        ) : (
                          <ChevronRight className="h-3.5 w-3.5" />
                        )}
                        查看依据
                      </button>
                      <button
                        type="button"
                        onClick={() => keepCard(item)}
                        disabled={isKept || isKeeping}
                        className="inline-flex items-center gap-1 rounded-lg border border-indigo-500/30 px-3 py-2 text-xs text-indigo-200 transition-colors hover:border-indigo-400/50 disabled:cursor-not-allowed disabled:border-zinc-800 disabled:text-zinc-500"
                      >
                        {isKeeping ? "处理中..." : isKept ? "已放入候选" : "值得记住"}
                      </button>
                    </div>
                  )}
                  {isExpanded && sources.length > 0 && (
                    <div className="mt-3 space-y-2">
                      {sources.map(source => (
                        <article key={source.id} className="rounded-xl border border-zinc-800 bg-zinc-950/30 p-3">
                          <p className="break-words text-sm font-medium leading-relaxed text-zinc-200">
                            {source.title || source.content}
                          </p>
                          <p className="mt-1 text-xs text-zinc-600">
                            {formatShortChineseDateTime(source.occurredAt)}
                          </p>
                          {source.content && source.content !== source.title && (
                            <p className="mt-2 break-words text-xs leading-relaxed text-zinc-500">
                              {source.content}
                            </p>
                          )}
                        </article>
                      ))}
                    </div>
                  )}
                </section>
              );
            })}

            <section className="rounded-2xl border border-zinc-800 bg-zinc-900/20 p-5">
              <h2 className="mb-3 text-base font-semibold text-zinc-100">最近记录</h2>
              {recentEvents.length === 0 ? (
                <p className="text-sm text-zinc-500">记录多一点后，我会帮你整理最近的变化。</p>
              ) : (
                <div className="space-y-3">
                  {recentEvents.map(event => (
                    <article key={event.id} className="rounded-xl border border-zinc-800 bg-zinc-950/30 p-3">
                      <p className="break-words text-sm font-medium leading-relaxed text-zinc-200">
                        {event.title || event.content}
                      </p>
                      <p className="mt-1 text-xs text-zinc-600">
                        {formatShortChineseDateTime(event.occurredAt)}
                      </p>
                    </article>
                  ))}
                </div>
              )}
            </section>
          </div>
        )}
      </div>
    </main>
  );
}

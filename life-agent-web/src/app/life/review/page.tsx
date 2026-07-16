"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import {
  ArrowLeft,
  Brain,
  CalendarDays,
  ChevronDown,
  ChevronRight,
  ClipboardList,
  Sparkles,
} from "lucide-react";
import {
  getLifeReview,
  keepLifeReviewCard,
  type LifeReviewCard,
  type LifeReviewContinuityHint,
  type LifeReviewPeriod,
  type LifeReviewSourceEvent,
  type LifeReviewTheme,
} from "@/app/actions/lifeReview";
import { useAuth } from "@/providers/AuthProvider";
import { formatShortChineseDateTime } from "@/lib/dateFormat";
import { ReviewCardSkeleton } from "@/components/LoadingSkeletons";
import { invalidatePageDataCache, loadPageDataCache, PageDataCacheInvalidatedError, pageDataCacheKey, readPageDataCache } from "@/lib/pageDataCache";

function focusQueryLabel(focus: string | null) {
  if (focus === "memory_related") return "和已记住背景相关的近期变化";
  if (focus === "recent_pattern") return "近期反复出现的主题";
  if (focus === "due_reminder") return "和提醒有关的回顾";
  return null;
}

function focusQueryDescription(focus: string | null) {
  if (focus === "memory_related") return "优先看看哪些记录、记忆或计划正在形成连续主线。";
  if (focus === "recent_pattern") return "优先看看重复出现的记录是否值得继续追踪或沉淀。";
  if (focus === "due_reminder") return "优先看看提醒前后的生活记录和上下文。";
  return null;
}

function continuityHintScore(hint: LifeReviewContinuityHint, focus: string | null) {
  if (!focus) return 0;

  const searchable = `${hint.kind} ${hint.label} ${hint.detail} ${hint.reason} ${hint.href}`.toLowerCase();
  if (focus === "memory_related" && (searchable.includes("memory") || searchable.includes("记忆"))) return 10;
  if (focus === "recent_pattern" && (searchable.includes("review") || searchable.includes("回顾"))) return 10;
  if (focus === "due_reminder" && (searchable.includes("reminder") || searchable.includes("提醒"))) return 10;

  return 0;
}

export default function LifeReviewPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [cards, setCards] = useState<LifeReviewCard[]>([]);
  const [reviewThemes, setReviewThemes] = useState<LifeReviewTheme[]>([]);
  const [continuityHints, setContinuityHints] = useState<LifeReviewContinuityHint[]>([]);
  const [sourceEvents, setSourceEvents] = useState<LifeReviewSourceEvent[]>([]);
  const [usedMemoryCount, setUsedMemoryCount] = useState(0);
  const [usedPlanSignalCount, setUsedPlanSignalCount] = useState(0);
  const [expandedCards, setExpandedCards] = useState<Record<string, boolean>>({});
  const [activePeriod, setActivePeriod] = useState<LifeReviewPeriod>("recent");
  const [keptCardIds, setKeptCardIds] = useState<Record<string, boolean>>({});
  const [keepingCardId, setKeepingCardId] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [focusQuery, setFocusQuery] = useState<string | null>(null);

  useEffect(() => {
    const frameId = window.requestAnimationFrame(() => {
      const focus = new URLSearchParams(window.location.search).get("focus")?.trim() || null;
      setFocusQuery(focus);
    });

    return () => window.cancelAnimationFrame(frameId);
  }, []);

  useEffect(() => {
    if (!user) return;

    let cancelled = false;
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Shanghai";
    const cacheKey = pageDataCacheKey("lifeReview", user.uid, activePeriod, 30, timeZone);
    const cached = readPageDataCache<Awaited<ReturnType<typeof getLifeReview>>>(cacheKey);
    let cachedTimer: number | undefined;
    let loadingTimer: number | undefined;

    const applyReview = (review: Awaited<ReturnType<typeof getLifeReview>>) => {
      setCards(review.cards ?? []);
      setReviewThemes(review.reviewThemes ?? []);
      setContinuityHints(review.continuityHints ?? []);
      setSourceEvents(review.sourceEvents ?? []);
      setUsedMemoryCount(review.usedMemoryCount ?? 0);
      setUsedPlanSignalCount(review.usedPlanSignalCount ?? 0);
      setExpandedCards({});
    };

    if (cached) {
      cachedTimer = window.setTimeout(() => {
        if (!cancelled) {
          applyReview(cached.value);
          setError(null);
          setIsLoading(false);
        }
      }, 0);

      if (cached.isFresh) {
        return () => {
          cancelled = true;
          if (cachedTimer !== undefined) {
            window.clearTimeout(cachedTimer);
          }
        };
      }
    } else {
      loadingTimer = window.setTimeout(() => {
        if (!cancelled) {
          setIsLoading(true);
          setError(null);
        }
      }, 0);
    }

    const load = async () => {
      try {
        const review = await loadPageDataCache(cacheKey, () => getLifeReview(timeZone, 30, activePeriod));

        if (!cancelled) {
          applyReview(review);
          setError(null);
        }
      } catch (err) {
        if (err instanceof PageDataCacheInvalidatedError) {
          return;
        }
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "暂时无法整理最近回顾");
          if (!cached) {
            setCards([]);
            setReviewThemes([]);
            setContinuityHints([]);
            setSourceEvents([]);
            setUsedMemoryCount(0);
            setUsedPlanSignalCount(0);
          }
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
      if (cachedTimer !== undefined) {
        window.clearTimeout(cachedTimer);
      }
      if (loadingTimer !== undefined) {
        window.clearTimeout(loadingTimer);
      }
    };
  }, [user, activePeriod]);

  const sourceEventMap = useMemo(() => {
    return new Map(sourceEvents.map(event => [event.id, event]));
  }, [sourceEvents]);

  const recentEvents = sourceEvents.slice(0, 5);
  const contextParts = [`${sourceEvents.length} 条生活记录`];
  if (usedMemoryCount > 0) {
    contextParts.push(`${usedMemoryCount} 条已记住内容`);
  }
  if (usedPlanSignalCount > 0) {
    contextParts.push(`${usedPlanSignalCount} 条计划线索`);
  }
  const contextSummary = cards.length > 0
    ? `基于 ${contextParts.join("、")}整理出 ${cards.length} 个回顾点`
    : null;
  const focusLabel = focusQueryLabel(focusQuery);
  const focusDescription = focusQueryDescription(focusQuery);
  const visibleContinuityHints = useMemo(() => {
    return [...continuityHints].sort((left, right) =>
      continuityHintScore(right, focusQuery) - continuityHintScore(left, focusQuery));
  }, [continuityHints, focusQuery]);

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
      if (user) {
        invalidatePageDataCache([
          pageDataCacheKey("memoryReview", user.uid),
          pageDataCacheKey("homeOverview", user.uid),
        ]);
      }
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
          {(loading || user) && (
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

        {loading ? (
          <ReviewCardSkeleton />
        ) : !user ? (
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
          <ReviewCardSkeleton />
        ) : error ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5 text-sm text-zinc-500">
            暂时无法整理最近回顾。你可以稍后再来看看。
          </div>
        ) : (
          <div className="space-y-5">
            {focusLabel && (
              <section className="rounded-2xl border border-violet-500/20 bg-violet-500/5 p-4">
                <p className="text-xs text-violet-200">从首页关注项进入</p>
                <h2 className="mt-1 text-base font-semibold text-zinc-100">{focusLabel}</h2>
                {focusDescription && (
                  <p className="mt-2 text-sm leading-relaxed text-zinc-500">{focusDescription}</p>
                )}
                <p className="mt-2 text-xs leading-relaxed text-zinc-600">
                  这里只做只读回顾。需要记住的内容仍要你点击“值得记住”后再进入候选。
                </p>
              </section>
            )}
            {(contextSummary || cards.length > 0) && (
              <div className="rounded-2xl border border-zinc-800 bg-zinc-900/25 p-4 text-sm leading-relaxed text-zinc-500">
                {contextSummary}
                {contextSummary && "。"}
                {" "}如果某个回顾点以后还会用到，可以先放进「可能值得记住的事」。
              </div>
            )}
            {reviewThemes.length > 0 && (
              <section className="rounded-2xl border border-indigo-500/15 bg-indigo-500/5 p-4">
                <div className="mb-3 flex items-center gap-2">
                  <Brain className="h-4 w-4 text-indigo-200" />
                  <h2 className="text-sm font-semibold text-zinc-100">近期复盘主线</h2>
                </div>
                <div className="space-y-2.5">
                  {reviewThemes.map(theme => {
                    const hints = theme.evidenceHints ?? [];
                    const isPlanTheme = theme.kind === "plan_progress";

                    return (
                      <Link
                        key={theme.id}
                        href={theme.href || (isPlanTheme ? "/plans" : "/memory")}
                        className={`block rounded-xl border px-3 py-3 transition-colors ${
                          isPlanTheme
                            ? "border-cyan-500/15 bg-cyan-500/5 hover:border-cyan-400/30"
                            : "border-indigo-500/15 bg-zinc-950/25 hover:border-indigo-400/30"
                        }`}
                      >
                        <div className="mb-1.5 flex items-start justify-between gap-3">
                          <div className="flex min-w-0 items-center gap-1.5">
                            {isPlanTheme ? (
                              <ClipboardList className="mt-0.5 h-3.5 w-3.5 shrink-0 text-cyan-200" />
                            ) : (
                              <Brain className="mt-0.5 h-3.5 w-3.5 shrink-0 text-indigo-200" />
                            )}
                            <p className="min-w-0 break-words text-sm font-medium leading-relaxed text-zinc-100">
                              {theme.title}
                            </p>
                          </div>
                          <span className="shrink-0 text-xs text-zinc-500">
                            {theme.actionLabel || (isPlanTheme ? "查看计划" : "查看记忆")}
                          </span>
                        </div>
                        <p className="break-words text-xs leading-relaxed text-zinc-500">
                          {theme.summary}
                        </p>
                        {hints.length > 0 && (
                          <div className="mt-2 flex flex-wrap gap-1.5">
                            {hints.slice(0, 2).map((hint, index) => (
                              <span
                                key={`${theme.id}-${hint.kind}-${index}`}
                                className={`rounded-full border px-2 py-1 text-[11px] ${
                                  hint.kind === "memory"
                                    ? "border-indigo-500/15 bg-indigo-500/5 text-indigo-200"
                                    : "border-cyan-500/15 bg-cyan-500/5 text-cyan-200"
                                }`}
                              >
                                {hint.label}
                              </span>
                            ))}
                          </div>
                        )}
                      </Link>
                    );
                  })}
                </div>
              </section>
            )}
            {visibleContinuityHints.length > 0 && (
              <section className="rounded-2xl border border-zinc-800 bg-zinc-900/25 p-4">
                <div className="mb-3 flex items-center gap-2">
                  <Sparkles className="h-4 w-4 text-zinc-300" />
                  <h2 className="text-sm font-semibold text-zinc-100">接下来可以</h2>
                </div>
                <div className="grid gap-2 sm:grid-cols-2">
                  {visibleContinuityHints.map(hint => (
                    <Link
                      key={hint.id || hint.href}
                      href={hint.href}
                      className="rounded-xl border border-zinc-800 bg-zinc-950/30 px-3 py-3 transition-colors hover:border-zinc-700"
                    >
                      <p className="text-sm font-medium text-zinc-100">{hint.label}</p>
                      <p className="mt-1 break-words text-xs leading-relaxed text-zinc-500">{hint.detail}</p>
                      <p className="mt-2 break-words text-[11px] leading-relaxed text-zinc-600">{hint.reason}</p>
                    </Link>
                  ))}
                </div>
              </section>
            )}
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
              const evidenceHints = item.evidenceHints ?? [];

              return (
                <section key={item.id} className="rounded-2xl border border-zinc-800 bg-zinc-900/35 p-5">
                  <div className="mb-3 flex items-center gap-2">
                    <Sparkles className="h-4 w-4 text-indigo-300" />
                    <h2 className="text-base font-semibold text-zinc-100">{item.title}</h2>
                  </div>
                  <p className="break-words text-sm leading-relaxed text-zinc-400">
                    {item.text}
                  </p>
                  {evidenceHints.length > 0 && (
                    <div className="mt-4 space-y-2">
                      {evidenceHints.map((hint, index) => (
                        <Link
                          key={`${hint.kind}-${index}-${hint.text}`}
                          href={hint.href || (hint.kind === "memory" ? "/memory" : "/plans")}
                          className={`block rounded-xl border px-3 py-2.5 transition-colors ${
                            hint.kind === "memory"
                              ? "border-indigo-500/15 bg-indigo-500/5 hover:border-indigo-400/30"
                              : "border-cyan-500/15 bg-cyan-500/5 hover:border-cyan-400/30"
                          }`}
                        >
                          <div className={`mb-1 flex items-center gap-1.5 text-xs ${
                            hint.kind === "memory" ? "text-indigo-200" : "text-cyan-200"
                          }`}>
                            {hint.kind === "memory" ? (
                              <Brain className="h-3.5 w-3.5" />
                            ) : (
                              <ClipboardList className="h-3.5 w-3.5" />
                            )}
                            {hint.label || (hint.kind === "memory" ? "关联记忆" : "相关计划")}
                          </div>
                          <p className="break-words text-xs font-medium leading-relaxed text-zinc-200">
                            {hint.text}
                          </p>
                          <p className="mt-1 break-words text-xs leading-relaxed text-zinc-500">
                            {hint.reason}
                          </p>
                        </Link>
                      ))}
                    </div>
                  )}
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

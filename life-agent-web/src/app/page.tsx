"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import {
  ArrowRight,
  Bell,
  BookOpen,
  Brain,
  Calendar,
  ClipboardList,
  LogOut,
  MessageCircle,
  Sparkles
} from "lucide-react";
import { useAuth } from "@/providers/AuthProvider";
import { Timeline } from "@/components/Timeline";
import { AgentPreview } from "@/components/AgentPreview";
import {
  getHomeOverview,
  type HomeOverviewContextThread,
  type HomeOverviewDailyBrief,
  type HomeOverviewData,
  type HomeOverviewPlanSignal,
  type HomeOverviewReminder,
  type HomeOverviewTodayFocus
} from "@/app/actions/homeOverview";
import { PageContentSkeleton, TimelineSkeleton } from "@/components/LoadingSkeletons";
import { formatShortChineseDateTime } from "@/lib/dateFormat";
import { invalidatePageDataCache, loadPageDataCache, PageDataCacheInvalidatedError, pageDataCacheKey, readPageDataCache } from "@/lib/pageDataCache";

function focusTone(type: HomeOverviewTodayFocus["type"]) {
  if (type === "reminder") {
    return {
      border: "border-indigo-500/20",
      bg: "bg-indigo-500/10",
      text: "text-indigo-200",
      hover: "hover:border-indigo-400/40",
      button: "bg-indigo-600 hover:bg-indigo-500"
    };
  }

  if (type === "plan") {
    return {
      border: "border-cyan-500/20",
      bg: "bg-cyan-500/10",
      text: "text-cyan-200",
      hover: "hover:border-cyan-400/40",
      button: "bg-cyan-600 hover:bg-cyan-500"
    };
  }

  return {
    border: "border-emerald-500/20",
    bg: "bg-emerald-500/10",
    text: "text-emerald-200",
    hover: "hover:border-emerald-400/40",
    button: "bg-emerald-600 hover:bg-emerald-500"
  };
}

function focusTypeLabel(type: HomeOverviewTodayFocus["type"]) {
  if (type === "reminder") return "提醒";
  if (type === "plan") return "计划";
  return "个人整理";
}

function FocusIcon({ type, className }: { type: HomeOverviewTodayFocus["type"]; className?: string }) {
  if (type === "reminder") return <Bell className={className} />;
  if (type === "plan") return <ClipboardList className={className} />;
  return <Sparkles className={className} />;
}

function threadKindLabel(kind: HomeOverviewContextThread["kind"]) {
  if (kind === "goal") return "目标";
  if (kind === "routine") return "习惯";
  if (kind === "temporary_context") return "近期";
  return "主题";
}

function evidenceLabel(sourceType: HomeOverviewContextThread["evidence"][number]["sourceType"]) {
  if (sourceType === "memory") return "记忆";
  if (sourceType === "event") return "记录";
  if (sourceType === "reminder") return "提醒";
  if (sourceType === "plan") return "计划";
  return "依据";
}

interface AiActionLink {
  href: string;
  label: string;
  tone: "indigo" | "cyan" | "zinc";
}

function selectAiActionLinks({
  pendingReviewCandidateCount,
  memoryCount,
  planSignalCount,
}: {
  pendingReviewCandidateCount: number;
  memoryCount: number;
  planSignalCount: number;
}): AiActionLink[] {
  const candidates: AiActionLink[] = [
    ...(pendingReviewCandidateCount > 0
      ? [{ href: "/memory/review", label: `判断 ${pendingReviewCandidateCount} 条记忆线索`, tone: "indigo" as const }]
      : []),
    { href: "/life/review", label: "查看最近回顾", tone: "indigo" },
    ...(memoryCount > 0
      ? [{ href: "/memory", label: "查看我的记忆", tone: "zinc" as const }]
      : []),
    ...(planSignalCount > 0
      ? [{ href: "/plans", label: `查看 ${planSignalCount} 条计划线索`, tone: "cyan" as const }]
      : []),
    { href: "/life/chat", label: "问问最近状态", tone: "zinc" },
  ];

  const seen = new Set<string>();
  return candidates
    .filter(link => {
      if (seen.has(link.href)) {
        return false;
      }
      seen.add(link.href);
      return true;
    })
    .slice(0, 2);
}

function DailyActionCard({
  todayFocus,
  reminders,
  planSignals,
  pendingReminderCount,
  planSignalCount,
  isLoading,
}: {
  todayFocus?: HomeOverviewTodayFocus[];
  reminders: HomeOverviewReminder[];
  planSignals: HomeOverviewPlanSignal[];
  pendingReminderCount: number;
  planSignalCount: number;
  isLoading: boolean;
}) {
  const legacyFocus: HomeOverviewTodayFocus[] = [
    ...reminders.map(reminder => ({
      id: reminder.id,
      type: "reminder" as const,
      title: reminder.title,
      reason: formatShortChineseDateTime(reminder.dueAt),
      href: "/reminders",
      basis: "due_soon" as const,
    })),
    ...planSignals.map(signal => ({
      id: signal.id,
      type: "plan" as const,
      title: signal.title,
      reason: signal.content && signal.content !== signal.title ? signal.content : "最近保存的计划线索。",
      href: "/plans",
      basis: "recent_pattern" as const,
    })),
  ].slice(0, 3);
  const visibleFocus = todayFocus ?? legacyFocus;
  const hasItems = visibleFocus.length > 0;
  const primaryFocus = visibleFocus[0];
  const secondaryFocus = visibleFocus.slice(1);
  const visibleManagedItemCount = visibleFocus.filter(item => item.type === "reminder" || item.type === "plan").length;
  const hiddenManagedItemCount = Math.max(0, pendingReminderCount + planSignalCount - visibleManagedItemCount);
  const primaryTone = primaryFocus ? focusTone(primaryFocus.type) : null;

  return (
    <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5">
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">今天需要留意</h2>
          <p className="mt-1 text-sm text-zinc-500">按时间和你的个人背景，整理今天最值得关注的事。</p>
        </div>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          <div className="h-16 animate-pulse rounded-xl bg-zinc-800/40" />
          <div className="h-16 animate-pulse rounded-xl bg-zinc-800/30" />
        </div>
      ) : !hasItems ? (
        <p className="rounded-xl border border-zinc-800 bg-zinc-950/30 px-3 py-3 text-sm text-zinc-500">
          今天暂时没有需要优先关注的事。
        </p>
      ) : (
        <div className="space-y-3">
          {primaryFocus && primaryTone && (
            <Link
              href={primaryFocus.href}
              className={`block rounded-2xl border ${primaryTone.border} ${primaryTone.bg} p-4 transition-colors ${primaryTone.hover}`}
            >
              <div className="flex flex-wrap items-center gap-2">
                <span className={`inline-flex items-center gap-1.5 rounded-full border ${primaryTone.border} bg-zinc-950/30 px-2.5 py-1 text-xs ${primaryTone.text}`}>
                  <FocusIcon type={primaryFocus.type} className="h-3.5 w-3.5" />
                  {focusTypeLabel(primaryFocus.type)}
                </span>
                {(primaryFocus.priorityLabel || primaryFocus.priority) && (
                  <span className="rounded-full border border-zinc-700 bg-zinc-950/40 px-2.5 py-1 text-xs text-zinc-300">
                    {primaryFocus.priorityLabel || `优先级 ${primaryFocus.priority}`}
                  </span>
                )}
              </div>
              <div className="mt-3 break-words text-base font-semibold leading-relaxed text-zinc-100">{primaryFocus.title}</div>
              <p className="mt-1 break-words text-sm leading-relaxed text-zinc-300">{primaryFocus.reason}</p>
              {(primaryFocus.explanation || primaryFocus.actionLabel) && (
                <div className="mt-3 rounded-xl border border-zinc-800/80 bg-zinc-950/35 px-3 py-2">
                  {primaryFocus.explanation && (
                    <p className="text-xs leading-relaxed text-zinc-400">{primaryFocus.explanation}</p>
                  )}
                  <span className={`mt-2 inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-medium text-white ${primaryTone.button}`}>
                    {primaryFocus.actionLabel || "查看"}
                    <ArrowRight className="h-3.5 w-3.5" />
                  </span>
                </div>
              )}
            </Link>
          )}

          {secondaryFocus.length > 0 && (
            <div className="space-y-2">
              {secondaryFocus.map(item => {
                const tone = focusTone(item.type);
                return (
                  <Link
                    key={`${item.type}-${item.id}`}
                    href={item.href}
                    className={`block rounded-xl border px-3 py-3 transition-colors ${tone.border} ${tone.bg} ${tone.hover}`}
                  >
                    <div className={`mb-1 flex items-center gap-2 text-xs ${tone.text}`}>
                      <FocusIcon type={item.type} className="h-3.5 w-3.5" />
                      {focusTypeLabel(item.type)}
                      {(item.priorityLabel || item.priority) && (
                        <span className="text-zinc-500">· {item.priorityLabel || `优先级 ${item.priority}`}</span>
                      )}
                    </div>
                    <div className="break-words text-sm font-medium leading-relaxed text-zinc-100">{item.title}</div>
                    <p className="mt-1 break-words text-xs leading-relaxed text-zinc-500">{item.explanation || item.reason}</p>
                  </Link>
                );
              })}
            </div>
          )}
        </div>
      )}

      {hiddenManagedItemCount > 0 && (
        <p className="mt-3 text-xs text-zinc-600">
          另有 {hiddenManagedItemCount} 条提醒或计划线索，可进入对应页面查看。
        </p>
      )}
    </section>
  );
}

function DailyAiPanel({
  dailyBrief,
  contextThreads,
  reviewCandidateCount,
  pendingReviewCandidateCount,
  memoryCount,
  planSignalCount,
  isLoading,
  error,
}: {
  dailyBrief?: HomeOverviewDailyBrief;
  contextThreads?: HomeOverviewContextThread[];
  reviewCandidateCount: number;
  pendingReviewCandidateCount: number;
  memoryCount: number;
  planSignalCount: number;
  isLoading: boolean;
  error: string | null;
}) {
  const memoryCountText = isLoading ? "已记住 -- 条" : `已记住 ${memoryCount} 条`;
  const hasMemoryLoop = reviewCandidateCount > 0 || memoryCount > 0;
  const primaryThread = contextThreads?.[0];
  const hasBriefSignals = Boolean(dailyBrief?.signals?.some(signal => signal.basis !== "empty_context"));
  const summary = hasBriefSignals
    ? dailyBrief?.summary || "我会把最近值得关注的变化整理在这里。"
    : "继续记录后，我会把真正反复出现的事放到这里。";
  const actionLinks = selectAiActionLinks({
    pendingReviewCandidateCount,
    memoryCount,
    planSignalCount,
  });

  return (
    <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5">
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 text-indigo-300">
          <Sparkles className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
            <h2 className="text-lg font-semibold text-zinc-100">AI 帮你整理</h2>
            <span className="text-xs text-zinc-600">{memoryCountText}</span>
          </div>
          {!isLoading && hasMemoryLoop && (
            <div className="mt-2 flex flex-wrap gap-2">
              {pendingReviewCandidateCount > 0 && (
                <Link
                  href="/memory/review"
                  className="rounded-full border border-indigo-500/20 bg-indigo-500/10 px-2.5 py-1 text-xs text-indigo-200 transition-colors hover:border-indigo-400/40"
                >
                  {pendingReviewCandidateCount} 条待判断
                </Link>
              )}
              {memoryCount > 0 && (
                <Link
                  href="/memory"
                  className="rounded-full border border-zinc-700 bg-zinc-950/50 px-2.5 py-1 text-xs text-zinc-300 transition-colors hover:border-zinc-600"
                >
                  查看我的记忆
                </Link>
              )}
            </div>
          )}
          {isLoading ? (
            <div className="mt-3 space-y-2">
              <div className="h-12 animate-pulse rounded-xl bg-zinc-800/40" />
              <div className="h-20 animate-pulse rounded-xl bg-zinc-800/30" />
            </div>
          ) : error ? (
            <p className="mt-2 text-sm leading-relaxed text-zinc-500">
              暂时无法整理 AI 发现。你可以继续记录，稍后我再试一次。
            </p>
          ) : (
            <div className="mt-3 space-y-3">
              <p className="rounded-xl border border-zinc-800 bg-zinc-950/40 px-3 py-2 text-sm leading-relaxed text-zinc-200">
                {summary}
              </p>

              {primaryThread && (
                <Link
                  href={primaryThread.href || "/life/review"}
                  className="block rounded-xl border border-violet-500/20 bg-violet-500/10 px-3 py-3 transition-colors hover:border-violet-400/40"
                >
                  <div className="mb-2 flex flex-wrap items-center gap-2">
                    <span className="inline-flex items-center gap-1.5 rounded-full border border-violet-500/20 bg-zinc-950/30 px-2.5 py-1 text-xs text-violet-200">
                      <Brain className="h-3.5 w-3.5" />
                      近期主线 · {threadKindLabel(primaryThread.kind)}
                    </span>
                  </div>
                  <div className="break-words text-sm font-medium leading-relaxed text-zinc-100">{primaryThread.title}</div>
                  <p className="mt-1 break-words text-xs leading-relaxed text-zinc-400">{primaryThread.summary}</p>
                  {primaryThread.evidence.length > 0 && (
                    <p className="mt-2 line-clamp-2 text-xs leading-relaxed text-zinc-500">
                      依据：{primaryThread.evidence.slice(0, 2).map(evidence => `${evidenceLabel(evidence.sourceType)} · ${evidence.title}`).join("；")}
                    </p>
                  )}
                </Link>
              )}

              <div className="grid gap-2 sm:grid-cols-2">
                {actionLinks.map(link => (
                  <Link
                    key={`${link.href}-${link.label}`}
                    href={link.href}
                    className={`inline-flex items-center justify-between gap-2 rounded-xl border px-3 py-2.5 text-sm transition-colors ${
                      link.tone === "cyan"
                        ? "border-cyan-500/20 bg-cyan-500/10 text-cyan-200 hover:border-cyan-400/40"
                        : link.tone === "indigo"
                          ? "border-indigo-500/20 bg-indigo-500/10 text-indigo-200 hover:border-indigo-400/40"
                          : "border-zinc-800 bg-zinc-950/30 text-zinc-300 hover:border-zinc-700"
                    }`}
                  >
                    {link.label}
                    <ArrowRight className="h-3.5 w-3.5" />
                  </Link>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}

export default function Home() {
  const { user, loading, loginWithGoogle, logoutUser } = useAuth();
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [overviewRefreshTrigger, setOverviewRefreshTrigger] = useState(0);
  const [overview, setOverview] = useState<HomeOverviewData | null>(null);
  const [isLoadingOverview, setIsLoadingOverview] = useState(false);
  const [overviewError, setOverviewError] = useState<string | null>(null);

  useEffect(() => {
    if (!user) return;

    let cancelled = false;
    const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Shanghai";
    const cacheKey = pageDataCacheKey("homeOverview", user.uid, 20, timeZone);
    const cached = readPageDataCache<HomeOverviewData>(cacheKey);
    let cachedTimer: number | undefined;
    let loadingTimer: number | undefined;

    if (cached) {
      cachedTimer = window.setTimeout(() => {
        if (!cancelled) {
          setOverview(cached.value);
          setOverviewError(null);
          setIsLoadingOverview(false);
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
          setIsLoadingOverview(true);
          setOverviewError(null);
        }
      }, 0);
    }

    const loadOverview = async () => {
      try {
        const nextOverview = await loadPageDataCache(cacheKey, () => getHomeOverview(20, timeZone));
        if (!cancelled) {
          setOverview(nextOverview);
          setOverviewError(null);
        }
      } catch (error) {
        if (error instanceof PageDataCacheInvalidatedError) {
          return;
        }
        if (!cancelled) {
          setOverviewError(error instanceof Error ? error.message : "首页内容暂时不可用");
          if (!cached) {
            setOverview(null);
          }
        }
      } finally {
        if (!cancelled) {
          setIsLoadingOverview(false);
        }
      }
    };

    loadOverview();

    return () => {
      cancelled = true;
      if (cachedTimer !== undefined) {
        window.clearTimeout(cachedTimer);
      }
      if (loadingTimer !== undefined) {
        window.clearTimeout(loadingTimer);
      }
    };
  }, [overviewRefreshTrigger, user]);

  const handleLogin = async () => {
    try {
      await loginWithGoogle();
    } catch (error) {
      console.error("Login failed:", error);
    }
  };

  const handleLogout = async () => {
    try {
      await logoutUser();
    } catch (error) {
      console.error("Logout failed:", error);
    }
  };

  const isLoggedIn = !!user;
  const isOverviewPending = isLoggedIn && !overview && !overviewError;

  return (
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 selection:bg-indigo-500/30 md:px-10 md:py-10">
      <div className="mx-auto max-w-3xl">
        <header className="mb-8 border-b border-zinc-800/50 pb-6">
          <div className="flex items-start justify-between gap-4">
            <div>
              <h1 className="bg-gradient-to-r from-indigo-400 to-cyan-400 bg-clip-text text-2xl font-bold text-transparent">
                LifeOS
              </h1>
              <p className="mt-2 text-sm text-zinc-500">一本会思考的生活记录本。</p>
            </div>

            {isLoggedIn && (
              <button
                onClick={handleLogout}
                className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-sm text-zinc-500 transition-colors hover:bg-zinc-900 hover:text-zinc-200"
              >
                <LogOut className="h-4 w-4" />
                退出
              </button>
            )}
          </div>

          {isLoggedIn && (
            <nav className="mt-5 flex flex-wrap gap-2 text-xs">
              <Link
                href="/knowledge"
                className="inline-flex items-center gap-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 px-3 py-1.5 text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
              >
                <BookOpen className="h-3.5 w-3.5" />
                资料库
              </Link>
              <Link
                href="/chat"
                className="inline-flex items-center gap-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 px-3 py-1.5 text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
              >
                <MessageCircle className="h-3.5 w-3.5" />
                资料问答
              </Link>
              <Link
                href="/life/chat"
                className="inline-flex items-center gap-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 px-3 py-1.5 text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
              >
                <Sparkles className="h-3.5 w-3.5" />
                生活问答
              </Link>
              <Link
                href="/memory"
                className="inline-flex items-center gap-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 px-3 py-1.5 text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
              >
                <Brain className="h-3.5 w-3.5" />
                记忆
              </Link>
              <Link
                href="/reminders"
                className="inline-flex items-center gap-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 px-3 py-1.5 text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
              >
                <Bell className="h-3.5 w-3.5" />
                提醒事项
              </Link>
              <Link
                href="/plans"
                className="inline-flex items-center gap-1.5 rounded-full border border-zinc-800 bg-zinc-900/50 px-3 py-1.5 text-zinc-400 transition-colors hover:border-zinc-700 hover:text-zinc-200"
              >
                <ClipboardList className="h-3.5 w-3.5" />
                计划线索
              </Link>
            </nav>
          )}
        </header>

        {loading ? (
          <div className="space-y-8">
            <PageContentSkeleton />
            <PageContentSkeleton />
          </div>
        ) : !isLoggedIn ? (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <div className="mb-6 flex h-16 w-16 items-center justify-center rounded-2xl bg-indigo-500/10">
              <BookOpen className="h-8 w-8 text-indigo-400" />
            </div>
            <h2 className="mb-2 text-2xl font-semibold text-white">欢迎回来</h2>
            <p className="mb-8 max-w-sm text-zinc-500">登录后记录生活，并在之后重新找到它。</p>
            <button
              onClick={handleLogin}
              className="rounded-xl bg-white px-6 py-3 font-medium text-zinc-900 transition-colors hover:bg-zinc-100"
            >
              使用 Google 登录
            </button>
          </div>
        ) : (
          <div className="space-y-6">
            <AgentPreview
              onLifeRecordWritten={() => {
                invalidatePageDataCache([
                  pageDataCacheKey("homeOverview", user.uid),
                  pageDataCacheKey("memoryReview", user.uid),
                  pageDataCacheKey("plans", user.uid),
                  pageDataCacheKey("lifeReview", user.uid),
                ]);
                setRefreshTrigger(t => t + 1);
                setOverviewRefreshTrigger(t => t + 1);
              }}
            />
            <DailyActionCard
              todayFocus={overview?.todayFocus}
              reminders={overview?.pendingReminders ?? []}
              planSignals={overview?.planSignals ?? []}
              pendingReminderCount={overview?.pendingReminderCount ?? 0}
              planSignalCount={overview?.planSignalCount ?? 0}
              isLoading={isLoadingOverview || isOverviewPending}
            />
            {overview ? (
              <Timeline
                refreshTrigger={refreshTrigger}
                initialEvents={overview.recentEvents}
                initialHasMoreRecords={overview.hasMoreRecentEvents}
                deferInitialLoad
              />
            ) : overviewError ? (
              <Timeline refreshTrigger={refreshTrigger} />
            ) : (
              <div className="w-full max-w-2xl mx-auto">
                <div className="mb-4 flex items-center justify-between">
                  <h2 className="flex items-center gap-2 text-xl font-semibold text-zinc-100">
                    <Calendar className="h-5 w-5 text-indigo-400" />
                    最近生活记录
                  </h2>
                </div>
                <TimelineSkeleton />
              </div>
            )}
            <DailyAiPanel
              dailyBrief={overview?.dailyBrief}
              contextThreads={overview?.contextThreads}
              reviewCandidateCount={overview?.memoryReviewCandidateCount ?? 0}
              pendingReviewCandidateCount={overview?.memoryReviewPendingCandidateCount ?? overview?.memoryReviewCandidateCount ?? 0}
              memoryCount={overview?.memoryCount ?? 0}
              planSignalCount={overview?.planSignalCount ?? 0}
              isLoading={isLoadingOverview || isOverviewPending}
              error={overviewError}
            />
          </div>
        )}
      </div>
    </main>
  );
}

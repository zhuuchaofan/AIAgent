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
import type { MemoryInsight } from "@/app/actions/memoryInsights";
import {
  getHomeOverview,
  type HomeOverviewContextThread,
  type HomeOverviewDailyBrief,
  type HomeOverviewData,
  type HomeOverviewPlanSignal,
  type HomeOverviewReminder,
  type HomeOverviewTodayFocus
} from "@/app/actions/homeOverview";
import { InsightSkeleton, PageContentSkeleton, TimelineSkeleton } from "@/components/LoadingSkeletons";
import { formatShortChineseDateTime } from "@/lib/dateFormat";

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

function ContextThreadsCard({
  threads,
  isLoading,
}: {
  threads?: HomeOverviewContextThread[];
  isLoading: boolean;
}) {
  const visibleThreads = threads ?? [];

  return (
    <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5">
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">近期主线</h2>
          <p className="mt-1 text-sm text-zinc-500">把记忆、计划、提醒和最近记录串起来看。</p>
        </div>
      </div>

      {isLoading ? (
        <div className="space-y-2">
          <div className="h-20 animate-pulse rounded-xl bg-zinc-800/40" />
          <div className="h-20 animate-pulse rounded-xl bg-zinc-800/30" />
        </div>
      ) : visibleThreads.length === 0 ? (
        <p className="rounded-xl border border-zinc-800 bg-zinc-950/30 px-3 py-3 text-sm text-zinc-500">
          暂时还没有足够明确的近期主线。继续记录后，我会把反复出现的事整理出来。
        </p>
      ) : (
        <div className="space-y-3">
          {visibleThreads.map(thread => (
            <Link
              key={thread.id}
              href={thread.href || "/life/review"}
              className="block rounded-2xl border border-violet-500/20 bg-violet-500/10 p-4 transition-colors hover:border-violet-400/40"
            >
              <div className="flex flex-wrap items-center gap-2">
                <span className="inline-flex items-center gap-1.5 rounded-full border border-violet-500/20 bg-zinc-950/30 px-2.5 py-1 text-xs text-violet-200">
                  <Brain className="h-3.5 w-3.5" />
                  {threadKindLabel(thread.kind)}
                </span>
                {thread.priority > 0 && (
                  <span className="rounded-full border border-zinc-700 bg-zinc-950/40 px-2.5 py-1 text-xs text-zinc-300">
                    优先级 {thread.priority}
                  </span>
                )}
              </div>
              <div className="mt-3 break-words text-base font-semibold leading-relaxed text-zinc-100">{thread.title}</div>
              <p className="mt-1 break-words text-sm leading-relaxed text-zinc-300">{thread.summary}</p>
              {thread.explanation && (
                <p className="mt-3 rounded-xl border border-zinc-800/80 bg-zinc-950/35 px-3 py-2 text-xs leading-relaxed text-zinc-400">
                  {thread.explanation}
                </p>
              )}
              {thread.evidence.length > 0 && (
                <div className="mt-3 space-y-1.5">
                  {thread.evidence.slice(0, 3).map(evidence => (
                    <div
                      key={`${thread.id}-${evidence.sourceType}-${evidence.sourceId}`}
                      className="rounded-lg border border-zinc-800/70 bg-zinc-950/30 px-2.5 py-2"
                    >
                      <div className="text-xs text-violet-200">{evidenceLabel(evidence.sourceType)} · {evidence.title}</div>
                      {evidence.detail && (
                        <p className="mt-1 line-clamp-2 text-xs leading-relaxed text-zinc-500">{evidence.detail}</p>
                      )}
                    </div>
                  ))}
                </div>
              )}
              <span className="mt-3 inline-flex items-center gap-1.5 text-xs font-medium text-violet-200">
                {thread.actionLabel || "查看"}
                <ArrowRight className="h-3.5 w-3.5" />
              </span>
            </Link>
          ))}
        </div>
      )}
    </section>
  );
}

function DailyAiPanel({
  insights,
  dailyBrief,
  reviewCandidateCount,
  pendingReviewCandidateCount,
  memoryCount,
  planSignalCount,
  isLoading,
  error,
}: {
  insights: MemoryInsight[];
  dailyBrief?: HomeOverviewDailyBrief;
  reviewCandidateCount: number;
  pendingReviewCandidateCount: number;
  memoryCount: number;
  planSignalCount: number;
  isLoading: boolean;
  error: string | null;
}) {
  const reviewLinkText = pendingReviewCandidateCount > 0
    ? `查看 ${pendingReviewCandidateCount} 条待确认线索`
    : "查看可能值得记住的事";
  const memoryCountText = isLoading ? "已记住 -- 条" : `已记住 ${memoryCount} 条`;
  const hasMemoryLoop = reviewCandidateCount > 0 || memoryCount > 0;
  const hasDailyBrief = Boolean(dailyBrief?.summary);

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
            <InsightSkeleton />
          ) : error ? (
            <p className="mt-2 text-sm leading-relaxed text-zinc-500">
              暂时无法整理 AI 发现。你可以继续记录，稍后我再试一次。
            </p>
          ) : hasDailyBrief ? (
            <div className="mt-3 space-y-2">
              <p className="rounded-xl border border-zinc-800 bg-zinc-950/40 px-3 py-2 text-sm leading-relaxed text-zinc-200">
                {dailyBrief?.summary}
              </p>
              {(dailyBrief?.signals ?? []).map(signal => (
                <Link
                  key={signal.id}
                  href={signal.href || "/"}
                  className="block rounded-xl border border-zinc-800 bg-zinc-950/30 px-3 py-3 transition-colors hover:border-zinc-700"
                >
                  <div className="text-sm font-medium leading-relaxed text-zinc-300">{signal.title}</div>
                  <p className="mt-1 text-xs leading-relaxed text-zinc-500">{signal.detail}</p>
                  {signal.explanation && (
                    <p className="mt-2 rounded-lg border border-zinc-800/70 bg-zinc-900/40 px-2.5 py-2 text-xs leading-relaxed text-zinc-500">
                      {signal.explanation}
                    </p>
                  )}
                  {signal.actionLabel && (
                    <span className="mt-2 inline-flex items-center gap-1.5 text-xs font-medium text-indigo-300">
                      {signal.actionLabel}
                      <ArrowRight className="h-3.5 w-3.5" />
                    </span>
                  )}
                </Link>
              ))}
            </div>
          ) : insights.length === 0 ? (
            <p className="mt-2 text-sm leading-relaxed text-zinc-500">
              记录多一点后，我会帮你看见反复出现的主题。
            </p>
          ) : (
            <ul className="mt-3 space-y-2">
              {insights.map((insight, index) => (
                <li
                  key={`${insight.kind}-${index}`}
                  className="rounded-xl border border-zinc-800 bg-zinc-950/40 px-3 py-2 text-sm leading-relaxed text-zinc-300"
                >
                  {insight.text}
                </li>
              ))}
            </ul>
          )}
          <Link
            href="/life/chat"
            className="mt-4 inline-flex text-sm text-indigo-300 transition-colors hover:text-indigo-200"
          >
            问问最近状态
          </Link>
          <Link
            href="/life/review"
            className="mt-2 block text-sm text-indigo-300 transition-colors hover:text-indigo-200"
          >
            查看最近回顾
          </Link>
          <Link
            href="/memory/review"
            className="mt-2 block text-sm text-indigo-300 transition-colors hover:text-indigo-200"
          >
            {reviewLinkText}
          </Link>
          {planSignalCount > 0 && (
            <Link
              href="/plans"
              className="mt-2 block text-sm text-cyan-300 transition-colors hover:text-cyan-200"
            >
              查看 {planSignalCount} 条计划线索
            </Link>
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

    const loadOverview = async () => {
      setIsLoadingOverview(true);
      setOverviewError(null);
      try {
        const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Shanghai";
        const nextOverview = await getHomeOverview(20, timeZone);
        if (!cancelled) {
          setOverview(nextOverview);
        }
      } catch (error) {
        if (!cancelled) {
          setOverviewError(error instanceof Error ? error.message : "首页内容暂时不可用");
          setOverview(null);
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
          <div className="space-y-8">
            <AgentPreview
              onLifeRecordWritten={() => {
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
            <ContextThreadsCard
              threads={overview?.contextThreads}
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
              insights={overview?.insights ?? []}
              dailyBrief={overview?.dailyBrief}
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

"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { Bell, BookOpen, Brain, Loader2, LogOut, MessageCircle, Sparkles } from "lucide-react";
import { useAuth } from "@/providers/AuthProvider";
import { Timeline } from "@/components/Timeline";
import { AgentPreview } from "@/components/AgentPreview";
import { getMemoryInsightPreview, type MemoryInsight } from "@/app/actions/memoryInsights";
import { getMemoryReviewInboxPreview } from "@/app/actions/memoryReview";
import { getMemoryItems } from "@/app/actions/memoryItems";

function InsightCard({
  insights,
  reviewCandidateCount,
  memoryCount,
  isLoading,
  error,
}: {
  insights: MemoryInsight[];
  reviewCandidateCount: number;
  memoryCount: number;
  isLoading: boolean;
  error: string | null;
}) {
  const reviewLinkText = reviewCandidateCount > 0
    ? `查看 ${reviewCandidateCount} 条可能值得记住的事`
    : "查看可能值得记住的事";

  return (
    <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5">
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 text-indigo-300">
          <Sparkles className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <h2 className="text-lg font-semibold text-zinc-100">AI 发现</h2>
          {isLoading ? (
            <div className="mt-3 flex items-center gap-2 text-sm text-zinc-500">
              <Loader2 className="h-4 w-4 animate-spin" />
              正在整理最近的线索...
            </div>
          ) : error ? (
            <p className="mt-2 text-sm leading-relaxed text-zinc-500">
              暂时无法整理 AI 发现。你可以继续记录，稍后我再试一次。
            </p>
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
          <Link
            href="/memory"
            className="mt-2 block text-sm text-zinc-500 transition-colors hover:text-zinc-300"
          >
            已记住 {memoryCount} 条
          </Link>
        </div>
      </div>
    </section>
  );
}

export default function Home() {
  const { user, loading, loginWithGoogle, logoutUser } = useAuth();
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [insightRefreshTrigger, setInsightRefreshTrigger] = useState(0);
  const [insights, setInsights] = useState<MemoryInsight[]>([]);
  const [reviewCandidateCount, setReviewCandidateCount] = useState(0);
  const [memoryCount, setMemoryCount] = useState(0);
  const [isLoadingInsights, setIsLoadingInsights] = useState(false);
  const [insightError, setInsightError] = useState<string | null>(null);

  const handleRecentEventsChange = useCallback(() => {
    setInsightRefreshTrigger(t => t + 1);
  }, []);

  useEffect(() => {
    if (!user) return;

    let cancelled = false;

    const loadInsights = async () => {
      setIsLoadingInsights(true);
      setInsightError(null);
      try {
        const [preview, reviewPreview, memoryItems] = await Promise.all([
          getMemoryInsightPreview(20),
          getMemoryReviewInboxPreview(20),
          getMemoryItems("active"),
        ]);
        if (!cancelled) {
          setInsights(preview.insights ?? []);
          setReviewCandidateCount(reviewPreview.candidates?.length ?? 0);
          setMemoryCount(memoryItems.length);
        }
      } catch (error) {
        if (!cancelled) {
          setInsightError(error instanceof Error ? error.message : "AI 发现暂时不可用");
          setInsights([]);
          setReviewCandidateCount(0);
          setMemoryCount(0);
        }
      } finally {
        if (!cancelled) {
          setIsLoadingInsights(false);
        }
      }
    };

    loadInsights();

    return () => {
      cancelled = true;
    };
  }, [insightRefreshTrigger, user]);

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

  if (loading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-950">
        <Loader2 className="h-8 w-8 animate-spin text-zinc-500" />
      </div>
    );
  }

  const isLoggedIn = !!user;

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
                提醒
              </Link>
            </nav>
          )}
        </header>

        {!isLoggedIn ? (
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
                setInsightRefreshTrigger(t => t + 1);
              }}
            />
            <Timeline refreshTrigger={refreshTrigger} onRecentEventsChange={handleRecentEventsChange} />
            <InsightCard
              insights={insights}
              reviewCandidateCount={reviewCandidateCount}
              memoryCount={memoryCount}
              isLoading={isLoadingInsights}
              error={insightError}
            />
          </div>
        )}
      </div>
    </main>
  );
}

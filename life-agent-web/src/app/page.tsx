"use client";

import Link from "next/link";
import { useCallback, useMemo, useState } from "react";
import { BookOpen, Loader2, LogOut, MessageCircle, Sparkles } from "lucide-react";
import { useAuth } from "@/providers/AuthProvider";
import { Timeline } from "@/components/Timeline";
import { AgentPreview } from "@/components/AgentPreview";
import type { LifeEvent } from "@/app/actions/events";

const GENERIC_TAGS = new Set(["生活日常", "未分类", "life"]);

function getInsightText(events: LifeEvent[]): string {
  if (events.length === 0) {
    return "写下第一条记录后，我会开始帮你整理最近的生活线索。";
  }

  const tags = events
    .flatMap(event => event.tags ?? [])
    .map(tag => tag.trim())
    .filter(tag => tag && !GENERIC_TAGS.has(tag));

  const topTags = Array.from(new Set(tags)).slice(0, 3);
  if (topTags.length > 0) {
    return `最近常出现：${topTags.map(tag => `#${tag}`).join("、")}。先继续记录，我会帮你把线索串起来。`;
  }

  if (events.length < 3) {
    return "最近记录还不多。再写几条后，我会帮你看见反复出现的主题。";
  }

  return `最近已经留下 ${events.length} 条片段。先把日常记下来，之后我会帮你整理值得回看的线索。`;
}

function InsightCard({ events }: { events: LifeEvent[] }) {
  const insightText = useMemo(() => getInsightText(events), [events]);

  return (
    <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-5">
      <div className="flex items-start gap-3">
        <div className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 text-indigo-300">
          <Sparkles className="h-4 w-4" />
        </div>
        <div>
          <h2 className="text-lg font-semibold text-zinc-100">AI 发现</h2>
          <p className="mt-2 text-sm leading-relaxed text-zinc-500">
            {insightText}
          </p>
        </div>
      </div>
    </section>
  );
}

export default function Home() {
  const { user, loading, loginWithGoogle, logoutUser } = useAuth();
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [recentEvents, setRecentEvents] = useState<LifeEvent[]>([]);

  const handleRecentEventsChange = useCallback((events: LifeEvent[]) => {
    setRecentEvents(events);
  }, []);

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
                问答
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
            <AgentPreview onLifeRecordWritten={() => setRefreshTrigger(t => t + 1)} />
            <Timeline refreshTrigger={refreshTrigger} onRecentEventsChange={handleRecentEventsChange} />
            <InsightCard events={recentEvents} />
          </div>
        )}
      </div>
    </main>
  );
}

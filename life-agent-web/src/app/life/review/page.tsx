"use client";

import Link from "next/link";
import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, CalendarDays, Loader2, Sparkles } from "lucide-react";
import { getEvents, type LifeEvent } from "@/app/actions/events";
import { getMemoryInsightPreview, type MemoryInsight } from "@/app/actions/memoryInsights";
import { useAuth } from "@/providers/AuthProvider";
import { formatShortChineseDateTime } from "@/lib/dateFormat";

function insightByKind(insights: MemoryInsight[], kinds: MemoryInsight["kind"][]) {
  return insights.find(insight => kinds.includes(insight.kind))?.text;
}

export default function LifeReviewPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [events, setEvents] = useState<LifeEvent[]>([]);
  const [insights, setInsights] = useState<MemoryInsight[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!user) return;

    let cancelled = false;

    const load = async () => {
      setIsLoading(true);
      setError(null);
      try {
        const [eventResult, insightResult] = await Promise.all([
          getEvents(undefined, undefined, 12),
          getMemoryInsightPreview(20),
        ]);

        if (!cancelled) {
          setEvents(eventResult.data ?? []);
          setInsights(insightResult.insights ?? []);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : "暂时无法整理最近回顾");
          setEvents([]);
          setInsights([]);
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
  }, [user]);

  const reviewItems = useMemo(() => {
    return [
      {
        title: "最近状态",
        text: insightByKind(insights, ["temporary_context", "goal"]) || "最近记录还在积累中，暂时先展示下面的具体记录。",
      },
      {
        title: "反复出现",
        text: insightByKind(insights, ["theme", "habit"]) || "暂时还看不出稳定重复的主题。",
      },
      {
        title: "可能值得留意",
        text: insightByKind(insights, ["preference"]) || "继续记录后，我会把更稳定的偏好和变化放在这里。",
      },
    ];
  }, [insights]);

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
            {reviewItems.map(item => (
              <section key={item.title} className="rounded-2xl border border-zinc-800 bg-zinc-900/35 p-5">
                <div className="mb-3 flex items-center gap-2">
                  <Sparkles className="h-4 w-4 text-indigo-300" />
                  <h2 className="text-base font-semibold text-zinc-100">{item.title}</h2>
                </div>
                <p className="break-words text-sm leading-relaxed text-zinc-400">
                  {item.text}
                </p>
              </section>
            ))}

            <section className="rounded-2xl border border-zinc-800 bg-zinc-900/20 p-5">
              <h2 className="mb-3 text-base font-semibold text-zinc-100">最近记录</h2>
              {events.length === 0 ? (
                <p className="text-sm text-zinc-500">记录多一点后，我会帮你整理最近的变化。</p>
              ) : (
                <div className="space-y-3">
                  {events.slice(0, 5).map(event => (
                    <article key={event.id} className="rounded-xl border border-zinc-800 bg-zinc-950/30 p-3">
                      <p className="break-words text-sm font-medium leading-relaxed text-zinc-200">
                        {event.title || event.content}
                      </p>
                      <p className="mt-1 text-xs text-zinc-600">
                        {formatShortChineseDateTime(event.occurredAt || event.createdAt || "")}
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

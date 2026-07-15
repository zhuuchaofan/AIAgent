"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowLeft, Archive, Bell, Check, ClipboardList, Loader2, LogOut } from "lucide-react";
import {
  archivePlanSignal,
  convertPlanSignalToReminder,
  getPlanSignals,
  type PlanSignal
} from "@/app/actions/planSignals";
import { useAuth } from "@/providers/AuthProvider";
import { PageContentSkeleton } from "@/components/LoadingSkeletons";
import { formatShortChineseDateTime } from "@/lib/dateFormat";

function kindLabel(kind: string): string {
  return kind === "reminder_signal" ? "提醒线索" : "计划";
}

function kindClass(kind: string): string {
  return kind === "reminder_signal"
    ? "border-amber-500/30 bg-amber-500/10 text-amber-200"
    : "border-cyan-500/30 bg-cyan-500/10 text-cyan-200";
}

export default function PlansPage() {
  const { user, loading, loginWithGoogle, logoutUser } = useAuth();
  const [signals, setSignals] = useState<PlanSignal[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [archivingId, setArchivingId] = useState<string | null>(null);
  const [convertingId, setConvertingId] = useState<string | null>(null);
  const [dueAtInputs, setDueAtInputs] = useState<Record<string, string>>({});

  const loadSignals = useCallback(async () => {
    if (!user) return;

    setIsLoading(true);
    setError(null);
    try {
      setSignals(await getPlanSignals("active"));
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法读取计划线索");
      setSignals([]);
    } finally {
      setIsLoading(false);
    }
  }, [user]);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void loadSignals();
    }, 0);

    return () => window.clearTimeout(timer);
  }, [loadSignals]);

  const grouped = useMemo(() => {
    return {
      plans: signals.filter(signal => signal.kind !== "reminder_signal"),
      reminderSignals: signals.filter(signal => signal.kind === "reminder_signal"),
    };
  }, [signals]);

  const handleArchive = async (id: string) => {
    setArchivingId(id);
    setError(null);
    setSuccessMessage(null);
    try {
      await archivePlanSignal(id);
      setSignals(current => current.filter(signal => signal.id !== id));
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法归档这条线索");
    } finally {
      setArchivingId(null);
    }
  };

  const handleConvertToReminder = async (signal: PlanSignal) => {
    const dueAtInput = dueAtInputs[signal.id]?.trim();
    if (!dueAtInput) {
      setError("请先为这条提醒线索补充时间");
      setSuccessMessage(null);
      return;
    }

    setConvertingId(signal.id);
    setError(null);
    setSuccessMessage(null);
    try {
      const dueAtDate = new Date(dueAtInput);
      if (Number.isNaN(dueAtDate.getTime())) {
        setError("请输入有效的提醒时间");
        return;
      }

      const dueAt = dueAtDate.toISOString();
      await convertPlanSignalToReminder(
        signal.id,
        dueAt,
        Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Shanghai",
        signal.title,
        signal.content
      );
      setSignals(current => current.filter(item => item.id !== signal.id));
      setDueAtInputs(current => {
        const next = { ...current };
        delete next[signal.id];
        return next;
      });
      setSuccessMessage("已保存为提醒；当前不会发送系统通知。");
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法保存为提醒");
    } finally {
      setConvertingId(null);
    }
  };

  if (!loading && !user) {
    return (
      <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 md:px-10 md:py-10">
        <div className="mx-auto flex max-w-2xl flex-col items-center justify-center py-20 text-center">
          <div className="mb-6 flex h-16 w-16 items-center justify-center rounded-2xl bg-cyan-500/10">
            <ClipboardList className="h-8 w-8 text-cyan-300" />
          </div>
          <h1 className="mb-2 text-2xl font-semibold text-white">计划线索</h1>
          <p className="mb-8 max-w-sm text-zinc-500">登录后查看你保存的计划和待补充提醒。</p>
          <button
            onClick={loginWithGoogle}
            className="rounded-xl bg-white px-6 py-3 font-medium text-zinc-900 transition-colors hover:bg-zinc-100"
          >
            使用 Google 登录
          </button>
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 selection:bg-cyan-500/30 md:px-10 md:py-10">
      <div className="mx-auto max-w-3xl">
        <header className="mb-8 border-b border-zinc-800/50 pb-6">
          <div className="mb-6 flex items-center justify-between gap-4">
            <Link
              href="/"
              className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-sm text-zinc-500 transition-colors hover:bg-zinc-900 hover:text-zinc-200"
            >
              <ArrowLeft className="h-4 w-4" />
              回到首页
            </Link>
            {user && (
              <button
                onClick={logoutUser}
                className="inline-flex items-center gap-1.5 rounded-lg px-2 py-1 text-sm text-zinc-500 transition-colors hover:bg-zinc-900 hover:text-zinc-200"
              >
                <LogOut className="h-4 w-4" />
                退出
              </button>
            )}
          </div>
          <div className="flex items-start gap-3">
            <div className="mt-1 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-cyan-500/10 text-cyan-300">
              <ClipboardList className="h-5 w-5" />
            </div>
            <div>
              <h1 className="text-3xl font-bold text-zinc-100">计划线索</h1>
              <p className="mt-3 max-w-xl text-sm leading-relaxed text-zinc-500">
                这里放还没变成提醒或任务的计划，以及缺少明确时间的提醒线索。
              </p>
            </div>
          </div>
        </header>

        {loading || isLoading ? (
          <PageContentSkeleton />
        ) : (
          <div className="space-y-5">
            {error && (
              <div className="rounded-2xl border border-amber-500/20 bg-amber-500/10 p-4 text-sm text-amber-200">
                {error}
              </div>
            )}
            {successMessage && (
              <div className="rounded-2xl border border-emerald-500/20 bg-emerald-500/10 p-4 text-sm text-emerald-200">
                {successMessage}
              </div>
            )}

            {signals.length === 0 ? (
              <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-6 text-sm leading-relaxed text-zinc-500">
                暂时没有计划线索。以后输入“下周准备……”或“之后想……”这类内容，我会先帮你放在这里。
              </div>
            ) : (
              <>
                <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-4">
                  <h2 className="mb-3 text-base font-semibold text-zinc-100">概览</h2>
                  <div className="grid grid-cols-2 gap-3">
                    <div className="rounded-xl border border-zinc-800 bg-zinc-950/30 p-3">
                      <div className="text-xs text-zinc-500">计划</div>
                      <div className="mt-1 text-2xl font-semibold text-zinc-100">{grouped.plans.length}</div>
                    </div>
                    <div className="rounded-xl border border-zinc-800 bg-zinc-950/30 p-3">
                      <div className="flex items-center gap-1 text-xs text-zinc-500">
                        <Bell className="h-3.5 w-3.5" />
                        待补充提醒
                      </div>
                      <div className="mt-1 text-2xl font-semibold text-zinc-100">{grouped.reminderSignals.length}</div>
                    </div>
                  </div>
                </section>

                <div className="space-y-3">
                  {signals.map(signal => (
                    <article key={signal.id} className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4">
                      <div className="flex items-start gap-3">
                        <div className="min-w-0 flex-1">
                          <div className="mb-2 flex flex-wrap items-center gap-2">
                            <span className={`rounded-full border px-2.5 py-1 text-xs font-medium ${kindClass(signal.kind)}`}>
                              {kindLabel(signal.kind)}
                            </span>
                            <span className="text-xs text-zinc-600">{formatShortChineseDateTime(signal.createdAt)}</span>
                          </div>
                          <h2 className="break-words text-base font-semibold leading-relaxed text-zinc-100">{signal.title}</h2>
                          {signal.content && signal.content !== signal.title && (
                            <p className="mt-2 break-words text-sm leading-relaxed text-zinc-500">{signal.content}</p>
                          )}
                          {signal.kind === "reminder_signal" && (
                            <div className="mt-3 rounded-xl border border-amber-500/10 bg-amber-500/5 p-3">
                              <p className="text-xs leading-relaxed text-amber-100/80">
                                这条提醒还缺少明确时间，所以暂时不会出现在提醒事项里。
                              </p>
                              <div className="mt-3 grid gap-2 sm:grid-cols-[1fr_auto]">
                                <input
                                  type="datetime-local"
                                  value={dueAtInputs[signal.id] ?? ""}
                                  onChange={event => {
                                    const value = event.target.value;
                                    setDueAtInputs(current => ({ ...current, [signal.id]: value }));
                                  }}
                                  className="min-w-0 rounded-xl border border-zinc-800 bg-zinc-950 px-3 py-2 text-sm text-zinc-100 outline-none transition-colors focus:border-amber-400"
                                  aria-label="提醒时间"
                                />
                                <button
                                  type="button"
                                  onClick={() => handleConvertToReminder(signal)}
                                  disabled={convertingId === signal.id}
                                  className="inline-flex items-center justify-center gap-1.5 rounded-xl border border-emerald-500/20 bg-emerald-500/10 px-3 py-2 text-sm text-emerald-100 transition-colors hover:bg-emerald-500/15 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                  {convertingId === signal.id ? (
                                    <Loader2 className="h-4 w-4 animate-spin" />
                                  ) : (
                                    <Check className="h-4 w-4" />
                                  )}
                                  保存为提醒
                                </button>
                              </div>
                            </div>
                          )}
                        </div>
                        <button
                          type="button"
                          onClick={() => handleArchive(signal.id)}
                          disabled={archivingId === signal.id}
                          className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-zinc-800 text-zinc-500 transition-colors hover:border-zinc-700 hover:text-zinc-200 disabled:opacity-50"
                          aria-label="归档线索"
                        >
                          {archivingId === signal.id ? <Loader2 className="h-4 w-4 animate-spin" /> : <Archive className="h-4 w-4" />}
                        </button>
                      </div>
                    </article>
                  ))}
                </div>
              </>
            )}
          </div>
        )}
      </div>
    </main>
  );
}

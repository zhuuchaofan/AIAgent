"use client";

import Link from "next/link";
import { useState } from "react";
import { ArrowLeft, Loader2, Search, ShieldCheck } from "lucide-react";
import { getEvents, type LifeEvent } from "@/app/actions/events";
import { useAuth } from "@/providers/AuthProvider";
import { formatShortChineseDateTime } from "@/lib/dateFormat";
import { getLifeEventCleanupCandidate, type LifeEventCleanupCandidate } from "@/lib/lifeEventDisplay";

const PAGE_SIZE = 100;
const MAX_SCAN_COUNT = 1000;

interface ScanResult {
  scannedCount: number;
  candidates: LifeEventCleanupCandidate[];
  stoppedByLimit: boolean;
}

async function scanLifeEvents(): Promise<ScanResult> {
  let cursor: string | null = null;
  let scannedCount = 0;
  const candidates: LifeEventCleanupCandidate[] = [];

  do {
    const response = await getEvents(cursor ?? undefined, undefined, PAGE_SIZE);
    const events = (response.data ?? []) as LifeEvent[];

    for (const event of events) {
      scannedCount += 1;
      const candidate = getLifeEventCleanupCandidate(event);
      if (candidate) {
        candidates.push(candidate);
      }
    }

    cursor = response.nextCursor ?? null;
  } while (cursor && scannedCount < MAX_SCAN_COUNT);

  return {
    scannedCount,
    candidates,
    stoppedByLimit: Boolean(cursor),
  };
}

export default function LifeEventsCleanupPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [isScanning, setIsScanning] = useState(false);
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleScan = async () => {
    if (isScanning) return;

    setIsScanning(true);
    setError(null);

    try {
      setScanResult(await scanLifeEvents());
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(message || "扫描失败，请稍后再试。");
    } finally {
      setIsScanning(false);
    }
  };

  if (loading) {
    return (
      <main className="flex min-h-screen items-center justify-center bg-zinc-950">
        <Loader2 className="h-8 w-8 animate-spin text-zinc-500" />
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 md:px-10 md:py-10">
      <div className="mx-auto max-w-4xl">
        <header className="mb-8 border-b border-zinc-800/50 pb-5">
          <Link href="/" className="mb-3 inline-flex items-center gap-1.5 text-sm text-zinc-500 hover:text-zinc-200">
            <ArrowLeft className="h-4 w-4" />
            回到首页
          </Link>
          <h1 className="text-2xl font-semibold text-zinc-100">生活记录清理预览</h1>
          <p className="mt-2 max-w-2xl text-sm leading-relaxed text-zinc-500">
            这里只扫描当前登录用户的生活记录，生成历史系统话术污染的只读报告。不会修改、删除或写入任何数据。
          </p>
        </header>

        {!user ? (
          <section className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6 text-center">
            <p className="text-sm text-zinc-500">请先登录，再查看清理预览。</p>
            <button
              onClick={loginWithGoogle}
              className="mt-5 rounded-xl bg-white px-5 py-2.5 text-sm font-medium text-zinc-900 hover:bg-zinc-100"
            >
              使用 Google 登录
            </button>
          </section>
        ) : (
          <div className="space-y-5">
            <section className="rounded-2xl border border-emerald-500/20 bg-emerald-500/10 p-4">
              <div className="flex items-start gap-3">
                <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-emerald-300" />
                <div>
                  <h2 className="text-sm font-semibold text-emerald-100">只读 dry-run</h2>
                  <p className="mt-1 text-sm leading-relaxed text-emerald-100/70">
                    本页面没有执行清理入口。真正修改线上数据需要单独计划和明确批准。
                  </p>
                </div>
              </div>
            </section>

            <section className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-5">
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div>
                  <h2 className="text-lg font-semibold text-zinc-100">扫描当前生活记录</h2>
                  <p className="mt-1 text-sm text-zinc-500">最多扫描最近 {MAX_SCAN_COUNT} 条记录。</p>
                </div>
                <button
                  type="button"
                  onClick={handleScan}
                  disabled={isScanning}
                  className="inline-flex items-center justify-center gap-2 rounded-xl bg-cyan-600 px-4 py-2.5 text-sm font-semibold text-white transition-colors hover:bg-cyan-700 disabled:opacity-40"
                >
                  {isScanning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
                  {isScanning ? "扫描中" : "开始扫描"}
                </button>
              </div>

              {error && (
                <div className="mt-4 rounded-xl border border-red-500/30 bg-red-500/10 px-3 py-2 text-sm text-red-200">
                  {error}
                </div>
              )}

              {scanResult && (
                <div className="mt-5 grid gap-3 text-sm sm:grid-cols-3">
                  <div className="rounded-xl border border-zinc-800 bg-zinc-950/50 p-3">
                    <div className="text-zinc-500">已扫描</div>
                    <div className="mt-1 text-xl font-semibold text-zinc-100">{scanResult.scannedCount}</div>
                  </div>
                  <div className="rounded-xl border border-zinc-800 bg-zinc-950/50 p-3">
                    <div className="text-zinc-500">候选记录</div>
                    <div className="mt-1 text-xl font-semibold text-zinc-100">{scanResult.candidates.length}</div>
                  </div>
                  <div className="rounded-xl border border-zinc-800 bg-zinc-950/50 p-3">
                    <div className="text-zinc-500">扫描状态</div>
                    <div className="mt-1 text-sm font-medium text-zinc-200">
                      {scanResult.stoppedByLimit ? "达到上限，仍有更多记录" : "已完成"}
                    </div>
                  </div>
                </div>
              )}
            </section>

            {scanResult && (
              <section className="space-y-3">
                {scanResult.candidates.length === 0 ? (
                  <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-6 text-sm text-zinc-500">
                    未发现需要清理的历史系统话术。
                  </div>
                ) : (
                  scanResult.candidates.map((candidate) => (
                    <article key={candidate.event.id} className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-4">
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                        <div>
                          <h3 className="font-semibold text-zinc-100">{candidate.proposedTitle}</h3>
                          <p className="mt-1 text-xs text-zinc-500">{formatShortChineseDateTime(candidate.event.occurredAt)}</p>
                        </div>
                        <div className="flex flex-wrap gap-2">
                          {candidate.reasons.map((reason) => (
                            <span key={reason} className="rounded-full border border-amber-500/30 bg-amber-500/10 px-2.5 py-1 text-xs text-amber-200">
                              {reason}
                            </span>
                          ))}
                        </div>
                      </div>

                      <div className="mt-4 grid gap-3 md:grid-cols-2">
                        <div className="rounded-xl border border-zinc-800 bg-zinc-950/50 p-3">
                          <div className="mb-2 text-xs font-medium text-zinc-500">当前内容</div>
                          <p className="break-words text-sm leading-relaxed text-zinc-300">{candidate.event.title || "无标题"}</p>
                          {candidate.event.content && (
                            <p className="mt-2 break-words text-sm leading-relaxed text-zinc-500">{candidate.event.content}</p>
                          )}
                        </div>
                        <div className="rounded-xl border border-cyan-500/20 bg-cyan-500/5 p-3">
                          <div className="mb-2 text-xs font-medium text-cyan-200/70">建议显示为</div>
                          <p className="break-words text-sm leading-relaxed text-zinc-100">{candidate.proposedTitle}</p>
                          {candidate.proposedContent && (
                            <p className="mt-2 break-words text-sm leading-relaxed text-zinc-400">{candidate.proposedContent}</p>
                          )}
                        </div>
                      </div>
                    </article>
                  ))
                )}
              </section>
            )}
          </div>
        )}
      </div>
    </main>
  );
}

"use client";

import { useState, useEffect, useCallback } from "react";
import {
  generateSummary,
  getSummaryByDate,
  DailySummaryData,
} from "@/app/actions/dailySummary";
import {
  Loader2,
  Sparkles,
  RefreshCw,
  CalendarDays,
  Smile,
  Star,
  Lightbulb,
  BookOpen,
} from "lucide-react";
import { useAuth } from "@/providers/AuthProvider";

interface Props {
  refreshTrigger: number;
}

// 将 IANA 时区 ID 转为 Intl 格式（浏览器已内置支持）
function getTodayLocalDate(timeZone: string): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone }).format(new Date());
}

// 情绪分到颜色映射
function getMoodColor(score: number | null): string {
  if (score === null) return "text-zinc-500";
  if (score >= 8) return "text-emerald-400";
  if (score >= 6) return "text-indigo-400";
  if (score >= 4) return "text-yellow-400";
  return "text-red-400";
}

// generatedBy 标签文字
function getGeneratedByLabel(by: string): string {
  return by === "llm" ? "AI 生成" : "空日";
}

export function DailySummaryCard({ refreshTrigger }: Props) {
  const { user } = useAuth();
  const [summary, setSummary] = useState<DailySummaryData | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isGenerating, setIsGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // 获取用户本地时区
  const timeZone =
    typeof window !== "undefined"
      ? Intl.DateTimeFormat().resolvedOptions().timeZone
      : "Asia/Shanghai";

  const todayDate = getTodayLocalDate(timeZone);

  const fetchTodaySummary = useCallback(async () => {
    try {
      const result = await getSummaryByDate(todayDate);
      setSummary(result?.data ?? null);
    } catch {
      // 查询失败不显示错误，只显示"未生成"
      setSummary(null);
    }
  }, [todayDate]);

  useEffect(() => {
    if (!user) return;
    const load = async () => {
      await Promise.resolve();
      setIsLoading(true);
      try {
        await fetchTodaySummary();
      } finally {
        setIsLoading(false);
      }
    };
    load();
  }, [fetchTodaySummary, refreshTrigger, user]);

  const handleGenerate = async (force = false) => {
    if (isGenerating) return;
    setIsGenerating(true);
    setError(null);
    try {
      const result = await generateSummary(todayDate, timeZone, force);
      setSummary(result.data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "生成失败，请稍后重试");
    } finally {
      setIsGenerating(false);
    }
  };

  return (
    <div className="bg-zinc-900/50 border border-zinc-800 rounded-2xl p-5 backdrop-blur-md shadow-xl">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-zinc-100 flex items-center gap-2">
          <CalendarDays className="w-5 h-5 text-indigo-400" />
          今日总结
        </h2>
        <span className="text-xs text-zinc-500 font-mono">{todayDate}</span>
      </div>

      {/* Loading state */}
      {isLoading ? (
        <div className="flex justify-center py-10">
          <Loader2 className="w-6 h-6 animate-spin text-zinc-500" />
        </div>
      ) : summary ? (
        /* ── 已有总结内容 ── */
        <div className="space-y-4 animate-in fade-in duration-500">
          {/* Summary text */}
          <div className="bg-zinc-950/50 border border-zinc-800 rounded-xl p-4">
            <div className="flex items-center gap-1.5 mb-2">
              <BookOpen className="w-3.5 h-3.5 text-indigo-400" />
              <span className="text-xs font-medium text-indigo-400 uppercase tracking-wider">
                总结
              </span>
            </div>
            <p className="text-sm text-zinc-300 leading-relaxed">{summary.summary}</p>
          </div>

          {/* Mood */}
          {summary.moodLabel && summary.moodLabel !== "暂无记录" && (
            <div className="flex items-center gap-3 px-1">
              <Smile className="w-4 h-4 text-zinc-400 shrink-0" />
              <span className="text-sm text-zinc-400">情绪：</span>
              <span
                className={`text-sm font-semibold ${getMoodColor(summary.moodScore)}`}
              >
                {summary.moodLabel}
              </span>
              {summary.moodScore !== null && (
                <span className="text-xs text-zinc-600 ml-auto font-mono">
                  {summary.moodScore.toFixed(1)} / 10
                </span>
              )}
            </div>
          )}

          {/* Highlights */}
          {summary.highlights.length > 0 && (
            <div>
              <div className="flex items-center gap-1.5 mb-2 px-1">
                <Star className="w-3.5 h-3.5 text-yellow-400" />
                <span className="text-xs font-medium text-yellow-400 uppercase tracking-wider">
                  高光时刻
                </span>
              </div>
              <ul className="space-y-1.5">
                {summary.highlights.map((h, i) => (
                  <li key={i} className="flex items-start gap-2">
                    <span className="text-yellow-500/50 mt-0.5 text-xs">✦</span>
                    <span className="text-sm text-zinc-300">{h}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Suggestions */}
          {summary.suggestions.length > 0 && (
            <div>
              <div className="flex items-center gap-1.5 mb-2 px-1">
                <Lightbulb className="w-3.5 h-3.5 text-cyan-400" />
                <span className="text-xs font-medium text-cyan-400 uppercase tracking-wider">
                  小建议
                </span>
              </div>
              <ul className="space-y-1.5">
                {summary.suggestions.map((s, i) => (
                  <li key={i} className="flex items-start gap-2">
                    <span className="text-cyan-500/50 mt-0.5 text-xs">→</span>
                    <span className="text-sm text-zinc-400">{s}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Footer meta */}
          <div className="flex items-center justify-between pt-2 border-t border-zinc-800/50">
            <span className="text-[10px] text-zinc-600">
              {getGeneratedByLabel(summary.generatedBy)} · {summary.eventCount} 条记录
            </span>
            <button
              onClick={() => handleGenerate(true)}
              disabled={isGenerating}
              title="重新生成"
              className="flex items-center gap-1 text-[11px] text-zinc-500 hover:text-indigo-400 transition-colors disabled:opacity-50"
            >
              {isGenerating ? (
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              ) : (
                <RefreshCw className="w-3.5 h-3.5" />
              )}
              重新生成
            </button>
          </div>
        </div>
      ) : (
        /* ── 尚未生成 ── */
        <div className="flex flex-col items-center justify-center py-8 text-center border border-dashed border-zinc-800 rounded-xl">
          <Sparkles className="w-8 h-8 text-indigo-400/50 mb-3" />
          <p className="text-sm text-zinc-500 mb-4">今日总结尚未生成</p>
          <button
            onClick={() => handleGenerate(false)}
            disabled={isGenerating}
            id="btn-generate-summary"
            className="flex items-center gap-2 px-4 py-2 bg-indigo-600/20 hover:bg-indigo-600/30 border border-indigo-500/30 text-indigo-400 rounded-xl text-sm font-medium transition-all disabled:opacity-50"
          >
            {isGenerating ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                生成中…
              </>
            ) : (
              <>
                <Sparkles className="w-4 h-4" />
                生成今日总结
              </>
            )}
          </button>
        </div>
      )}

      {/* Error message */}
      {error && (
        <div className="mt-3 p-3 bg-red-500/10 border border-red-500/20 rounded-lg text-xs text-red-400">
          ⚠️ {error}
        </div>
      )}
    </div>
  );
}

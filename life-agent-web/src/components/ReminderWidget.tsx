"use client";

import { useEffect, useState, useCallback } from "react";
import { getReminders, updateReminder } from "@/app/actions/reminders";
import { format } from "date-fns";
import { Loader2, Bell, Clock, Check, X, AlertTriangle } from "lucide-react";
import { useAuth } from "@/providers/AuthProvider";

interface Reminder {
  id: string;
  sourceEventId: string;
  title: string;
  description?: string;
  dueAt: string;
  timezone: string;
  status: string;
  displayStatus: string;
  repeatRule: string;
  createdAt: string;
  updatedAt: string;
}

export function ReminderWidget({
  refreshTrigger,
  onUpdated,
}: {
  refreshTrigger: number;
  onUpdated: () => void;
}) {
  const { user } = useAuth();
  const [reminders, setReminders] = useState<Reminder[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [actioningId, setActioningId] = useState<string | null>(null);

  const fetchReminders = useCallback(async () => {
    try {
      const data = await getReminders("pending");
      if (data.success) {
        setReminders(data.data || []);
      }
    } catch (err) {
      console.error("Failed to fetch reminders:", err);
    }
  }, []);

  useEffect(() => {
    if (!user) return;
    const load = async () => {
      await Promise.resolve();
      setIsLoading(true);
      try {
        await fetchReminders();
      } finally {
        setIsLoading(false);
      }
    };
    load();
  }, [fetchReminders, refreshTrigger, user]);

  const handleAction = async (id: string, status: "completed" | "cancelled") => {
    if (actioningId) return;
    setActioningId(id);
    try {
      await updateReminder(id, { status });
      // 成功后前端本地过滤掉该提醒，并回调父组件刷新 Timeline
      setReminders((prev) => prev.filter((r) => r.id !== id));
      onUpdated();
    } catch (err: any) {
      alert("更新提醒失败: " + err.message);
    } finally {
      setActioningId(null);
    }
  };

  return (
    <div className="bg-zinc-900/50 border border-zinc-800 p-5 rounded-2xl backdrop-blur-md shadow-xl">
      <h2 className="text-lg font-semibold text-zinc-100 flex items-center gap-2 mb-4">
        <Bell className="w-5 h-5 text-indigo-400" />
        提醒事项
      </h2>

      {isLoading ? (
        <div className="flex justify-center py-8">
          <Loader2 className="w-6 h-6 animate-spin text-zinc-500" />
        </div>
      ) : reminders.length === 0 ? (
        <div className="text-center py-8 text-zinc-500 border border-dashed border-zinc-800 rounded-xl text-sm">
          暂无待处理提醒事项。
        </div>
      ) : (
        <div className="space-y-3 max-h-[480px] overflow-y-auto pr-1">
          {reminders.map((reminder) => {
            const isOverdue = reminder.displayStatus === "overdue";
            const dueTime = new Date(reminder.dueAt);

            return (
              <div
                key={reminder.id}
                className={`p-4 rounded-xl border transition-all ${
                  isOverdue
                    ? "bg-red-500/5 border-red-500/20 hover:border-red-500/30"
                    : "bg-zinc-950/40 border-zinc-850 hover:border-zinc-700"
                }`}
              >
                <div className="flex justify-between items-start gap-2 mb-1.5">
                  <h3 className="font-medium text-sm text-zinc-200 line-clamp-2">
                    {reminder.title}
                  </h3>
                  {isOverdue && (
                    <span className="flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-red-500/10 text-red-400 border border-red-500/20 shrink-0">
                      <AlertTriangle className="w-2.5 h-2.5" />
                      已逾期
                    </span>
                  )}
                </div>

                {reminder.description && (
                  <p className="text-xs text-zinc-400 mb-3 line-clamp-3">
                    {reminder.description}
                  </p>
                )}

                <div className="flex justify-between items-center mt-2">
                  <div className="flex items-center gap-1 text-[11px] text-zinc-500 font-mono">
                    <Clock className="w-3.5 h-3.5" />
                    {format(dueTime, "PPp")}
                  </div>

                  <div className="flex gap-1">
                    <button
                      onClick={() => handleAction(reminder.id, "completed")}
                      disabled={actioningId !== null}
                      title="标记已完成"
                      className="p-1 rounded-md text-zinc-400 hover:text-emerald-400 hover:bg-emerald-500/10 disabled:opacity-50 transition-colors"
                    >
                      {actioningId === reminder.id ? (
                        <Loader2 className="w-4 h-4 animate-spin" />
                      ) : (
                        <Check className="w-4 h-4" />
                      )}
                    </button>
                    <button
                      onClick={() => handleAction(reminder.id, "cancelled")}
                      disabled={actioningId !== null}
                      title="取消提醒"
                      className="p-1 rounded-md text-zinc-400 hover:text-red-400 hover:bg-red-500/10 disabled:opacity-50 transition-colors"
                    >
                      {actioningId === reminder.id ? (
                        <Loader2 className="w-4 h-4 animate-spin" />
                      ) : (
                        <X className="w-4 h-4" />
                      )}
                    </button>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

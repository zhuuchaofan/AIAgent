"use client";

import Link from "next/link";
import { useCallback, useState } from "react";
import { ArrowLeft, Bell, LogOut } from "lucide-react";
import { ReminderWidget } from "@/components/ReminderWidget";
import { useAuth } from "@/providers/AuthProvider";
import { PageContentSkeleton } from "@/components/LoadingSkeletons";

export default function RemindersPage() {
  const { user, loading, loginWithGoogle, logoutUser } = useAuth();
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  const refreshReminders = useCallback(() => {
    setRefreshTrigger((value) => value + 1);
  }, []);

  if (!loading && !user) {
    return (
      <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 md:px-10 md:py-10">
        <div className="mx-auto flex max-w-2xl flex-col items-center justify-center py-20 text-center">
          <div className="mb-6 flex h-16 w-16 items-center justify-center rounded-2xl bg-indigo-500/10">
            <Bell className="h-8 w-8 text-indigo-400" />
          </div>
          <h1 className="mb-2 text-2xl font-semibold text-white">提醒事项</h1>
          <p className="mb-8 max-w-sm text-zinc-500">登录后查看你确认保存的提醒。</p>
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
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 selection:bg-indigo-500/30 md:px-10 md:py-10">
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
            <div className="mt-1 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 text-indigo-300">
              <Bell className="h-5 w-5" />
            </div>
            <div>
              <h1 className="text-3xl font-bold text-zinc-100">提醒事项</h1>
              <p className="mt-3 max-w-xl text-sm leading-relaxed text-zinc-500">
                这里只显示待处理提醒，可以标记完成或取消；当前不会发送系统通知。
              </p>
            </div>
          </div>
        </header>

        {loading ? (
          <PageContentSkeleton />
        ) : (
          <ReminderWidget refreshTrigger={refreshTrigger} onUpdated={refreshReminders} />
        )}
      </div>
    </main>
  );
}

"use client";

import Link from "next/link";
import { ArrowLeft } from "lucide-react";
import { RagChat } from "@/components/RagChat";
import { useAuth } from "@/providers/AuthProvider";
import { PageContentSkeleton } from "@/components/LoadingSkeletons";

export default function ChatPage() {
  const { user, loading, loginWithGoogle } = useAuth();

  return (
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 md:px-10 md:py-10">
      <div className="mx-auto max-w-5xl">
        <header className="mb-8 flex items-center justify-between gap-4 border-b border-zinc-800/50 pb-5">
          <div>
            <Link href="/" className="mb-3 inline-flex items-center gap-1.5 text-sm text-zinc-500 hover:text-zinc-200">
              <ArrowLeft className="h-4 w-4" />
              回到首页
            </Link>
            <h1 className="text-2xl font-semibold text-zinc-100">资料问答</h1>
            <p className="mt-1 text-sm text-zinc-500">向你的资料库提问。</p>
          </div>
        </header>

        {loading ? (
          <PageContentSkeleton />
        ) : !user ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6 text-center">
            <p className="text-sm text-zinc-500">请先登录，再使用资料问答。</p>
            <button
              onClick={loginWithGoogle}
              className="mt-5 rounded-xl bg-white px-5 py-2.5 text-sm font-medium text-zinc-900 hover:bg-zinc-100"
            >
              使用 Google 登录
            </button>
          </div>
        ) : (
          <RagChat />
        )}
      </div>
    </main>
  );
}

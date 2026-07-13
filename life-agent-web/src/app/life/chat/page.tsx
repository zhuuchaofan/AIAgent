"use client";

import Link from "next/link";
import { FormEvent, useMemo, useState } from "react";
import { ArrowLeft, Brain, Loader2, Send } from "lucide-react";
import { askLifeChat } from "@/app/actions/lifeChat";
import { useAuth } from "@/providers/AuthProvider";

type Message = {
  id: string;
  role: "assistant" | "user";
  content: string;
  meta?: string;
};

const initialMessages: Message[] = [
  {
    id: "welcome",
    role: "assistant",
    content: "可以问我：我最近在关注什么？最近状态怎么样？哪些事情反复出现了？",
  },
];

export default function LifeChatPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [messages, setMessages] = useState<Message[]>(initialMessages);
  const [input, setInput] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canSend = useMemo(() => input.trim().length > 0 && !isSending, [input, isSending]);

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const message = input.trim();
    if (!message || isSending) return;

    const userMessage: Message = {
      id: `user-${Date.now()}`,
      role: "user",
      content: message,
    };

    setMessages(prev => [...prev, userMessage]);
    setInput("");
    setError(null);
    setIsSending(true);

    try {
      const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || "Asia/Shanghai";
      const result = await askLifeChat(message, timeZone);
      setMessages(prev => [
        ...prev,
        {
          id: `assistant-${Date.now()}`,
          role: "assistant",
          content: result.response,
          meta: `基于 ${result.usedEventCount} 条生活记录和 ${result.usedMemoryCount} 条记忆，只读回答。`,
        },
      ]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法回答这个问题");
    } finally {
      setIsSending(false);
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
    <main className="min-h-screen bg-zinc-950 px-5 py-6 text-zinc-300 selection:bg-indigo-500/30 md:px-10 md:py-10">
      <div className="mx-auto flex min-h-[calc(100vh-3rem)] max-w-3xl flex-col">
        <header className="mb-6 border-b border-zinc-800/50 pb-6">
          <Link
            href="/"
            className="mb-5 inline-flex items-center gap-2 text-sm text-zinc-500 transition-colors hover:text-zinc-200"
          >
            <ArrowLeft className="h-4 w-4" />
            回到首页
          </Link>
          <div className="flex items-start gap-3">
            <div className="mt-1 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-indigo-500/10 text-indigo-300">
              <Brain className="h-5 w-5" />
            </div>
            <div>
              <h1 className="text-2xl font-semibold text-zinc-100">生活问答</h1>
              <p className="mt-2 text-sm leading-relaxed text-zinc-500">
                基于最近生活记录和已记住内容回答，不会写入或执行任何操作。
              </p>
            </div>
          </div>
        </header>

        {!user ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/40 p-6 text-center">
            <p className="text-sm text-zinc-500">请先登录，再向 LifeOS 提问。</p>
            <button
              onClick={loginWithGoogle}
              className="mt-5 rounded-xl bg-white px-5 py-2.5 text-sm font-medium text-zinc-900 hover:bg-zinc-100"
            >
              使用 Google 登录
            </button>
          </div>
        ) : (
          <section className="flex min-h-0 flex-1 flex-col rounded-2xl border border-zinc-800 bg-zinc-900/30">
            <div className="flex-1 space-y-4 overflow-y-auto p-4 md:p-5">
              {messages.map(message => (
                <article
                  key={message.id}
                  className={`flex ${message.role === "user" ? "justify-end" : "justify-start"}`}
                >
                  <div
                    className={`max-w-[88%] rounded-2xl px-4 py-3 text-sm leading-relaxed md:max-w-[78%] ${
                      message.role === "user"
                        ? "bg-indigo-500 text-white"
                        : "border border-zinc-800 bg-zinc-950/50 text-zinc-200"
                    }`}
                  >
                    <p className="whitespace-pre-wrap break-words">{message.content}</p>
                    {message.meta && (
                      <p className="mt-3 border-t border-zinc-800 pt-2 text-xs text-zinc-500">
                        {message.meta}
                      </p>
                    )}
                  </div>
                </article>
              ))}
              {isSending && (
                <div className="flex items-center gap-2 text-sm text-zinc-500">
                  <Loader2 className="h-4 w-4 animate-spin" />
                  正在整理你的生活记录...
                </div>
              )}
            </div>

            {error && (
              <div className="mx-4 mb-3 rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-sm text-amber-200 md:mx-5">
                {error}
              </div>
            )}

            <form onSubmit={handleSubmit} className="border-t border-zinc-800 p-4 md:p-5">
              <div className="flex items-end gap-2">
                <textarea
                  value={input}
                  onChange={event => setInput(event.target.value)}
                  placeholder="问问最近的状态、反复出现的主题，或某件事的背景..."
                  rows={2}
                  className="min-h-12 flex-1 resize-none rounded-xl border border-zinc-800 bg-zinc-950 px-4 py-3 text-sm text-zinc-100 outline-none transition-colors placeholder:text-zinc-600 focus:border-indigo-500/50"
                />
                <button
                  type="submit"
                  disabled={!canSend}
                  className="flex h-12 w-12 shrink-0 items-center justify-center rounded-xl bg-indigo-500 text-white transition-colors hover:bg-indigo-400 disabled:cursor-not-allowed disabled:bg-zinc-800 disabled:text-zinc-600"
                  aria-label="发送问题"
                  title="发送问题"
                >
                  {isSending ? <Loader2 className="h-5 w-5 animate-spin" /> : <Send className="h-5 w-5" />}
                </button>
              </div>
            </form>
          </section>
        )}
      </div>
    </main>
  );
}

"use client";

import Link from "next/link";
import { FormEvent, useMemo, useRef, useState } from "react";
import { ArrowLeft, Brain, Loader2, Send } from "lucide-react";
import { askLifeChat } from "@/app/actions/lifeChat";
import { Markdown } from "@/components/Markdown";
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
    content: "可以问我最近的状态、反复出现的事情，或某件事的背景。我会尽量用简短的方式帮你回顾。",
  },
];

const quickQuestions = [
  "最近状态",
  "反复出现",
  "近期计划",
];

function buildContextMeta(usedMemoryCount: number, usedReminderCount: number): string {
  const parts = ["最近记录"];

  if (usedReminderCount > 0) {
    parts.push(`${usedReminderCount} 条待处理提醒`);
  }

  if (usedMemoryCount > 0) {
    parts.push(`${usedMemoryCount} 条已记住内容`);
  }

  return `基于${parts.join("、")}整理`;
}

export default function LifeChatPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [messages, setMessages] = useState<Message[]>(initialMessages);
  const [input, setInput] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const messageIdRef = useRef(0);

  const canSend = useMemo(() => input.trim().length > 0 && !isSending, [input, isSending]);

  const sendMessage = async (text: string) => {
    const message = text.trim();
    if (!message || isSending) return;

    messageIdRef.current += 1;
    const userMessage: Message = {
      id: `user-${messageIdRef.current}`,
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
      messageIdRef.current += 1;
      setMessages(prev => [
        ...prev,
        {
          id: `assistant-${messageIdRef.current}`,
          role: "assistant",
          content: result.response,
          meta: buildContextMeta(result.usedMemoryCount, result.usedReminderCount),
        },
      ]);
    } catch (err) {
      setError(err instanceof Error ? err.message : "暂时无法回答这个问题");
    } finally {
      setIsSending(false);
    }
  };

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    await sendMessage(input);
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
                基于最近生活记录、待处理提醒和你确认过的记忆回答，不会写入或执行任何操作。
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
          <section className="relative flex min-h-[500px] min-w-0 flex-1 flex-col overflow-hidden rounded-2xl border border-zinc-800/40 bg-zinc-900/10 shadow-2xl">
            <div className="flex-1 space-y-5 overflow-y-auto p-5 min-h-[380px] min-w-0">
              <div className="flex flex-wrap gap-2 pl-11">
                {quickQuestions.map(question => (
                  <button
                    key={question}
                    type="button"
                    onClick={() => sendMessage(question)}
                    disabled={isSending}
                    className="rounded-full border border-zinc-800 bg-zinc-950/40 px-3 py-1.5 text-xs text-zinc-400 transition-colors hover:border-indigo-500/40 hover:text-indigo-200 disabled:cursor-not-allowed disabled:opacity-50"
                  >
                    {question}
                  </button>
                ))}
              </div>

              {messages.map(message => (
                <div
                  key={message.id}
                  className={`flex max-w-[85%] min-w-0 gap-3 ${
                    message.role === "user" ? "ml-auto flex-row-reverse" : "mr-auto"
                  }`}
                >
                  <div
                    className={`flex h-8 w-8 shrink-0 select-none items-center justify-center rounded-full text-xs font-bold ${
                      message.role === "user"
                        ? "bg-zinc-800 text-zinc-300"
                        : "border border-indigo-500/20 bg-indigo-500/10 text-indigo-400"
                    }`}
                  >
                    {message.role === "user" ? "U" : "AI"}
                  </div>

                  <div className={`min-w-0 space-y-2 ${message.role === "user" ? "" : "flex-1"}`}>
                    <div
                      className={`min-w-0 rounded-2xl p-3.5 ${
                        message.role === "user"
                          ? "rounded-tr-none bg-indigo-600 text-white shadow-[0_4px_12px_rgba(99,102,241,0.15)]"
                          : "rounded-tl-none border border-zinc-800/50 bg-zinc-900/60"
                      }`}
                    >
                      {message.role === "user" ? (
                        <p className="break-words text-sm leading-relaxed text-white [overflow-wrap:anywhere]">
                          {message.content}
                        </p>
                      ) : (
                        <Markdown content={message.content} />
                      )}
                    </div>

                    {message.role === "assistant" && message.meta && (
                      <p className="px-1 text-[10px] text-zinc-600">
                        {message.meta}
                      </p>
                    )}
                  </div>
                </div>
              ))}
              {isSending && (
                <div className="mr-auto flex max-w-[80%] min-w-0 animate-pulse items-center gap-3">
                  <div
                    className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full border border-indigo-500/20 bg-indigo-500/10 text-xs font-bold text-indigo-400"
                  >
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  </div>
                  <div className="min-w-0 rounded-2xl rounded-tl-none border border-zinc-800/30 bg-zinc-900/40 p-3.5 text-xs text-zinc-500">
                    正在整理你的生活记录...
                  </div>
                </div>
              )}
            </div>

            {error && (
              <div className="mx-4 mb-3 rounded-xl border border-amber-500/20 bg-amber-500/10 p-3 text-sm text-amber-200 md:mx-5">
                {error}
              </div>
            )}

            <form onSubmit={handleSubmit} className="flex gap-2 border-t border-zinc-800/40 bg-zinc-900/20 p-4">
              <input
                type="text"
                value={input}
                onChange={event => setInput(event.target.value)}
                disabled={isSending}
                placeholder="问问最近的状态、反复出现的主题，或某件事的背景..."
                className="flex-1 rounded-xl border border-zinc-800/80 bg-zinc-950 px-4 py-2.5 text-sm text-white outline-none transition-colors placeholder:text-zinc-600 focus:border-indigo-500 disabled:opacity-50"
              />
              <button
                type="submit"
                disabled={!canSend}
                className="flex shrink-0 items-center justify-center rounded-xl bg-indigo-600 p-2.5 text-white transition-colors hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-30"
                aria-label="发送问题"
                title="发送问题"
              >
                {isSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
              </button>
            </form>
          </section>
        )}
      </div>
    </main>
  );
}

"use client";

import { useState } from "react";
import { ingestEvent } from "@/app/actions/events";
import { Send, Loader2 } from "lucide-react";

export function IngestForm({ onIngested }: { onIngested: () => void }) {
  const [text, setText] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!text.trim() || isSubmitting) return;

    setIsSubmitting(true);
    try {
      const tz = Intl.DateTimeFormat().resolvedOptions().timeZone;
      await ingestEvent(text, tz);
      setText("");
      onIngested();
    } catch (err) {
      const errMsg = err instanceof Error ? err.message : String(err);
      alert("记录失败: " + errMsg);
    } finally {
      setIsSubmitting(false);
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mb-8 w-full max-w-2xl mx-auto">
      <div className="relative group">
        <div className="absolute -inset-0.5 bg-gradient-to-r from-blue-500 to-indigo-500 rounded-2xl blur opacity-30 group-focus-within:opacity-100 transition duration-500"></div>
        <div className="relative bg-zinc-900 rounded-2xl overflow-hidden border border-zinc-800">
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="记录今天发生的事情，或添加一个提醒..."
            className="w-full bg-transparent text-zinc-100 p-4 min-h-[120px] resize-none focus:outline-none placeholder:text-zinc-500"
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                handleSubmit(e);
              }
            }}
          />
          <div className="flex justify-end p-3 border-t border-zinc-800 bg-zinc-900/50">
            <button
              type="submit"
              disabled={!text.trim() || isSubmitting}
              className="flex items-center gap-2 bg-indigo-600 hover:bg-indigo-500 text-white px-4 py-2 rounded-xl text-sm font-medium transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
            >
              {isSubmitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Send className="w-4 h-4" />}
              记录生活
            </button>
          </div>
        </div>
      </div>
    </form>
  );
}

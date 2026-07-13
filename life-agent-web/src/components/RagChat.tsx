"use client";

import React, { useState, useEffect, useRef, useMemo } from "react";
import { sendRagMessage, getRagChatHistory, clearRagChatHistory } from "@/app/actions/knowledge";
import { getMemoryItems } from "@/app/actions/memoryItems";
import { useAuth } from "@/providers/AuthProvider";
import { useDocuments } from "@/providers/DocumentProvider";
import { ChatInputBar } from "./rag-chat/ChatInputBar";
import { ChatMessageList } from "./rag-chat/ChatMessageList";
import { DocumentFilterPanel } from "./rag-chat/DocumentFilterPanel";
import { MemoryContextNotice } from "./rag-chat/MemoryContextNotice";
import { RagChatHeader } from "./rag-chat/RagChatHeader";
import type { CitationNode, RagChatMessage } from "./rag-chat/types";

const CONVERSATION_ID = "rag_default_session";
const WELCOME_MESSAGE: RagChatMessage = {
  role: "assistant",
  content: "你好，我可以帮你查找和总结知识库里的内容。你可以先选择文档范围，也可以直接提问。",
};

function initialMessages(): RagChatMessage[] {
  return [WELCOME_MESSAGE];
}

export function RagChat() {
  const { user } = useAuth();
  const { documents } = useDocuments();
  const docs = useMemo(() => documents.filter(d => d.status === "success"), [documents]);
  const availableDocIds = useMemo(() => docs.map(d => d.id), [docs]);
  const [selectedDocIds, setSelectedDocIds] = useState<string[]>([]);
  const [messages, setMessages] = useState<RagChatMessage[]>(initialMessages);
  const [inputValue, setInputValue] = useState("");
  const [loading, setLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const [memoryCount, setMemoryCount] = useState(0);
  const [clearing, setClearing] = useState(false);
  const [showClearConfirm, setShowClearConfirm] = useState(false);

  const chatEndRef = useRef<HTMLDivElement>(null);

  const loadHistory = async () => {
    try {
      const historyRes = await getRagChatHistory(CONVERSATION_ID);
      if (historyRes.success) {
        if (Array.isArray(historyRes.data) && historyRes.data.length > 0) {
          const mappedMessages: RagChatMessage[] = historyRes.data.map((m: { role: string; content: string; citations?: CitationNode[]; citationIntegrity?: string }) => ({
            role: m.role as "user" | "assistant",
            content: m.content,
            citations: m.citations || [],
            citationIntegrity: m.citationIntegrity
          }));
          setMessages([WELCOME_MESSAGE, ...mappedMessages]);
        } else {
          setMessages(initialMessages());
        }
      } else {
        setHistoryError("拉取历史记录失败：" + (historyRes.message || "未知错误"));
      }
    } catch (err: unknown) {
      console.error("Fetch chat history failed:", err);
      const errMsg = err instanceof Error ? err.message : String(err);
      setHistoryError("拉取历史记录连接异常：" + errMsg);
    }
  };

  useEffect(() => {
    if (!user) return;

    const initChat = async () => {
      setHistoryError(null);
      await loadHistory();
    };

    initChat();
  }, [user]);

  useEffect(() => {
    if (!user) return;

    let cancelled = false;
    const loadMemoryCount = async () => {
      try {
        const memories = await getMemoryItems("active");
        if (!cancelled) {
          setMemoryCount(memories.length);
        }
      } catch (err) {
        console.warn("Fetch memory count failed:", err);
        if (!cancelled) {
          setMemoryCount(0);
        }
      }
    };

    loadMemoryCount();

    return () => {
      cancelled = true;
    };
  }, [user]);

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

  useEffect(() => {
    const timer = setTimeout(() => {
      setSelectedDocIds(prev => {
        const next = prev.filter(id => availableDocIds.includes(id));
        return next.length === prev.length ? prev : next;
      });
    }, 0);

    return () => clearTimeout(timer);
  }, [availableDocIds]);

  const handleToggleDoc = (docId: string) => {
    setSelectedDocIds(prev =>
      prev.includes(docId)
        ? prev.filter(id => id !== docId)
        : [...prev, docId]
    );
  };

  const handleSelectAll = () => {
    if (selectedDocIds.length === docs.length) {
      setSelectedDocIds([]);
    } else {
      setSelectedDocIds(availableDocIds);
    }
  };

  const handleClearConversation = async () => {
    if (clearing) return;
    setClearing(true);
    try {
      await clearRagChatHistory(CONVERSATION_ID);
      setMessages(initialMessages());
      setHistoryError(null);
    } catch (err: unknown) {
      console.error("Clear conversation failed:", err);
      setMessages(initialMessages());
    } finally {
      setClearing(false);
      setShowClearConfirm(false);
    }
  };

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputValue.trim() || loading) return;

    const userMessageText = inputValue.trim();
    setInputValue("");
    setLoading(true);
    setMessages(prev => [...prev, { role: "user", content: userMessageText }]);

    try {
      const res = await sendRagMessage(
        CONVERSATION_ID,
        userMessageText,
        selectedDocIds.length > 0 ? selectedDocIds : undefined,
        "Asia/Shanghai"
      );

      setMessages(prev => [...prev, {
        role: "assistant",
        content: res.response,
        citations: res.citations || [],
        citationIntegrity: res.citationIntegrity
      }]);
    } catch (err: unknown) {
      console.error("RAG chat error:", err);
      const errMsg = err instanceof Error ? err.message : String(err);
      setMessages(prev => [...prev, {
        role: "assistant",
        content: "⚠️ 发送失败：" + errMsg,
      }]);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-4 gap-8 min-h-[580px] animate-in fade-in slide-in-from-bottom-3 duration-500">
      <DocumentFilterPanel
        docs={docs}
        selectedDocIds={selectedDocIds}
        onSelectAll={handleSelectAll}
        onToggleDoc={handleToggleDoc}
      />

      <div className="lg:col-span-3 bg-zinc-900/10 border border-zinc-800/40 rounded-2xl flex flex-col overflow-hidden shadow-2xl min-h-[500px] min-w-0 relative">
        <RagChatHeader
          selectedDocCount={selectedDocIds.length}
          loading={loading}
          clearing={clearing}
          onRequestClear={() => setShowClearConfirm(true)}
        />
        <MemoryContextNotice memoryCount={memoryCount} />
        <ChatMessageList
          messages={messages}
          loading={loading}
          historyError={historyError}
          chatEndRef={chatEndRef}
          onRetryHistory={loadHistory}
        />
        <ChatInputBar
          inputValue={inputValue}
          loading={loading}
          clearing={clearing}
          docsCount={docs.length}
          showClearConfirm={showClearConfirm}
          onInputChange={setInputValue}
          onSend={handleSend}
          onCancelClear={() => setShowClearConfirm(false)}
          onConfirmClear={handleClearConversation}
        />
      </div>
    </div>
  );
}

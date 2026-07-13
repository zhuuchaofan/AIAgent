"use client";

import React, { useState, useEffect, useRef, useMemo } from "react";
import {
  Send,
  BookOpen,
  Loader2,
  Info,
  AlertTriangle,
  FileText,
  Sparkles,
  Trash2
} from "lucide-react";
import { sendRagMessage, getRagChatHistory, clearRagChatHistory } from "@/app/actions/knowledge";
import { useAuth } from "@/providers/AuthProvider";
import { Markdown } from "./Markdown";
import { useDocuments } from "@/providers/DocumentProvider";

interface CitationNode {
  index: number;
  documentId: string;
  documentName: string;
  chunkIndex: number;
  pageNumber: number;
  sectionTitle: string | null;
  snippetPreview: string;
}

interface Message {
  role: "user" | "assistant";
  content: string;
  citations?: CitationNode[];
  citationIntegrity?: string;
}

export function RagChat() {
  const { user } = useAuth();
  const { documents } = useDocuments();
  const docs = useMemo(() => documents.filter(d => d.status === "success"), [documents]);
  const availableDocIds = useMemo(() => docs.map(d => d.id), [docs]);
  const [selectedDocIds, setSelectedDocIds] = useState<string[]>([]);
  const [messages, setMessages] = useState<Message[]>([
    {
      role: "assistant",
      content: "你好，我可以帮你查找和总结知识库里的内容。你可以先选择文档范围，也可以直接提问。",
    }
  ]);
  const [inputValue, setInputValue] = useState("");
  const [loading, setLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);
  const convId = "rag_default_session";

  const chatEndRef = useRef<HTMLDivElement>(null);

  const loadHistory = async () => {
    try {
      const historyRes = await getRagChatHistory(convId);
      if (historyRes.success) {
        if (Array.isArray(historyRes.data) && historyRes.data.length > 0) {
          const mappedMessages: Message[] = historyRes.data.map((m: { role: string; content: string; citations?: CitationNode[]; citationIntegrity?: string }) => ({
            role: m.role as "user" | "assistant",
            content: m.content,
            citations: m.citations || [],
            citationIntegrity: m.citationIntegrity
          }));
          setMessages([
            {
              role: "assistant",
              content: "你好，我可以帮你查找和总结知识库里的内容。你可以先选择文档范围，也可以直接提问。",
            },
            ...mappedMessages
          ]);
        } else {
          setMessages([
            {
              role: "assistant",
              content: "你好，我可以帮你查找和总结知识库里的内容。你可以先选择文档范围，也可以直接提问。",
            }
          ]);
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

  // 初始化会话并在组件挂载时拉取历史对话
  useEffect(() => {
    if (!user) return;

    const initChat = async () => {
      setHistoryError(null);
      await loadHistory();
    };

    initChat();
  }, [user]);

  // 自动滚动到聊天底部
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

  // 文档删除或状态变化后，清理已不可检索的选中文档，避免请求携带无效 documentId。
  useEffect(() => {
    const timer = setTimeout(() => {
      setSelectedDocIds(prev => {
        const next = prev.filter(id => availableDocIds.includes(id));
        return next.length === prev.length ? prev : next;
      });
    }, 0);

    return () => clearTimeout(timer);
  }, [availableDocIds]);

  // 文档勾选逻辑
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

  const [clearing, setClearing] = useState(false);
  const [showClearConfirm, setShowClearConfirm] = useState(false);

  // 清除当前对话
  const handleClearConversation = async () => {
    if (clearing) return;
    setClearing(true);
    try {
      // 调用后端清空接口
      await clearRagChatHistory(convId);
      // 重置前端 state 为仅保留欢迎消息
      setMessages([
        {
          role: "assistant",
          content: "你好，我可以帮你查找和总结知识库里的内容。你可以先选择文档范围，也可以直接提问。",
        }
      ]);
      setHistoryError(null);
    } catch (err: unknown) {
      console.error("Clear conversation failed:", err);
      // 即使后端清空失败，也清空前端状态
      setMessages([
        {
          role: "assistant",
          content: "你好，我可以帮你查找和总结知识库里的内容。你可以先选择文档范围，也可以直接提问。",
        }
      ]);
    } finally {
      setClearing(false);
      setShowClearConfirm(false);
    }
  };

  // 发送消息
  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputValue.trim() || loading) return;

    const userMessageText = inputValue.trim();
    setInputValue("");
    setLoading(true);

    // 1. 添加用户消息到前端 UI
    setMessages(prev => [...prev, { role: "user", content: userMessageText }]);

    try {
      // 2. 调用 RAG 问答 API
      const res = await sendRagMessage(
        convId,
        userMessageText,
        selectedDocIds.length > 0 ? selectedDocIds : undefined,
        "Asia/Shanghai"
      );

      // 3. 将助理的响应加入 UI
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

      {/* 左侧文档筛选面板 */}
      <div className="lg:col-span-1 bg-zinc-900/20 border border-zinc-800/50 rounded-2xl p-4 flex flex-col space-y-4 shadow-lg">
        <div className="flex items-center gap-2 border-b border-zinc-800/40 pb-3">
          <BookOpen className="w-4 h-4 text-indigo-400" />
          <h3 className="text-sm font-semibold text-white">指定检索文档</h3>
        </div>

        {docs.length === 0 ? (
          <div className="py-8 text-center text-zinc-600 text-xs">
            <Info className="w-8 h-8 text-zinc-800 mx-auto mb-2" />
            <p>暂无解析成功的文档</p>
            <p className="mt-1">请先前往「知识库管理」页面上传文档并等待解析完成。</p>
          </div>
        ) : (
          <div className="flex-1 flex flex-col space-y-3">
            <button
              onClick={handleSelectAll}
              className="text-left text-xs font-semibold text-indigo-400 hover:text-indigo-300 transition-colors py-1"
            >
              {selectedDocIds.length === docs.length ? "取消全选" : "全选所有可检索文档"}
            </button>
            <div className="space-y-2 max-h-[350px] overflow-y-auto pr-1">
              {docs.map(doc => (
                <label
                  key={doc.id}
                  className={`flex items-center gap-2.5 p-2 rounded-xl border text-xs cursor-pointer transition-all ${
                    selectedDocIds.includes(doc.id)
                      ? "bg-indigo-500/5 border-indigo-500/30 text-zinc-200"
                      : "bg-transparent border-zinc-800/40 text-zinc-400 hover:border-zinc-700 hover:text-zinc-300"
                  }`}
                >
                  <input
                    type="checkbox"
                    checked={selectedDocIds.includes(doc.id)}
                    onChange={() => handleToggleDoc(doc.id)}
                    className="accent-indigo-500 rounded border-zinc-700 focus:ring-0 focus:ring-offset-0"
                  />
                  <FileText className="w-3.5 h-3.5 text-zinc-500 shrink-0" />
                  <span className="truncate" title={doc.fileName}>{doc.fileName}</span>
                </label>
              ))}
            </div>
            <div className="pt-2 border-t border-zinc-800/40">
              <p className="text-[10px] text-zinc-500 leading-normal flex items-start gap-1">
                <Info className="w-3 h-3 text-zinc-600 shrink-0 mt-0.5" />
                若不勾选任何文档，后端将自动在整个知识库的所有文档中执行召回检索。
              </p>
            </div>
          </div>
        )}
      </div>

      {/* 右侧聊天窗口 */}
      <div className="lg:col-span-3 bg-zinc-900/10 border border-zinc-800/40 rounded-2xl flex flex-col overflow-hidden shadow-2xl min-h-[500px] min-w-0 relative">
        {/* 聊天窗头部 */}
        <div className="px-5 py-4 border-b border-zinc-800/40 bg-zinc-900/30 flex items-center justify-between">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <Sparkles className="w-4 h-4 shrink-0 text-indigo-400" />
              <h3 className="text-sm font-semibold text-white">知识库问答</h3>
            </div>
            <p className="mt-1 text-xs leading-relaxed text-zinc-600">回答主要来自你上传的文档；已记住的个人背景只作辅助参考。</p>
          </div>
          <div className="flex items-center gap-2">
            {selectedDocIds.length > 0 && (
              <span className="text-[10px] bg-indigo-500/10 text-indigo-400 px-2 py-0.5 rounded font-medium">
                限定 {selectedDocIds.length} 个文档进行回答
              </span>
            )}
            <button
              type="button"
              onClick={() => setShowClearConfirm(true)}
              disabled={loading || clearing}
              className="text-zinc-500 hover:text-red-400 p-1.5 rounded-lg hover:bg-red-500/10 transition-colors disabled:opacity-50"
              title="清除当前对话记录"
            >
              <Trash2 className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* 消息历史滚动区 */}
        <div className="flex-1 p-5 overflow-y-auto space-y-5 min-h-[380px] max-h-[480px] min-w-0">
          {historyError && (
            <div className="bg-red-500/10 border border-red-500/30 text-red-400 p-4 rounded-xl flex items-center justify-between gap-3 animate-in fade-in duration-300">
              <div className="flex items-center gap-2 text-xs">
                <AlertTriangle className="w-4 h-4 shrink-0 text-red-400" />
                <span>{historyError}</span>
              </div>
              <button
                type="button"
                onClick={loadHistory}
                className="bg-red-500/20 hover:bg-red-500/30 text-red-300 text-xs px-2.5 py-1 rounded-lg transition-colors font-medium shrink-0"
              >
                重试加载
              </button>
            </div>
          )}

          {messages.map((msg, index) => (
            <div
              key={index}
              className={`flex gap-3 max-w-[85%] min-w-0 ${
                msg.role === "user" ? "ml-auto flex-row-reverse" : "mr-auto"
              }`}
            >
              {/* 头像 */}
              <div className={`w-8 h-8 rounded-full shrink-0 flex items-center justify-center font-bold text-xs select-none ${
                msg.role === "user"
                  ? "bg-zinc-800 text-zinc-300"
                  : "bg-indigo-500/10 text-indigo-400 border border-indigo-500/20"
              }`}>
                {msg.role === "user" ? "U" : "AI"}
              </div>

              {/* 消息体卡片 */}
              <div className="space-y-2 min-w-0 flex-1">
                <div className={`p-3.5 rounded-2xl min-w-0 ${
                  msg.role === "user"
                    ? "bg-indigo-600 text-white rounded-tr-none shadow-[0_4px_12px_rgba(99,102,241,0.15)]"
                    : "bg-zinc-900/60 border border-zinc-800/50 rounded-tl-none"
                }`}>
                  {msg.role === "user"
                    ? <p className="whitespace-pre-wrap leading-relaxed text-sm text-white break-words [overflow-wrap:anywhere]">{msg.content}</p>
                    : <Markdown content={msg.content} citations={msg.citations} />
                  }
                </div>

                {/* 引用来源列表 (assistant only) */}
                {msg.role === "assistant" && msg.citations && msg.citations.length > 0 && (
                  <div className="mt-2.5 pt-2.5 border-t border-zinc-800/60 text-xs text-zinc-400 space-y-2 min-w-0">
                    <div className="font-semibold text-zinc-300 flex items-center gap-1">
                      <BookOpen className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
                      <span>引用来源</span>
                    </div>
                    <ul className="list-none pl-0 space-y-2 max-h-[200px] overflow-y-auto pr-1 min-w-0">
                      {msg.citations.map((c) => (
                        <li key={c.index} className="flex items-start gap-2 hover:text-zinc-200 transition-colors min-w-0">
                          <span className="inline-flex items-center justify-center w-4 h-4 text-[9px] font-bold text-indigo-400 bg-indigo-500/10 border border-indigo-500/20 rounded shrink-0 mt-0.5">
                            {c.index}
                          </span>
                          <div className="flex-1 min-w-0">
                            <span className="font-medium text-zinc-300 break-all block text-xs" title={c.documentName || c.documentId || "未知来源"}>
                              {c.documentName || c.documentId || "未知来源"}
                            </span>
                            <span className="text-[10px] text-zinc-500 block font-mono">
                              Page {c.pageNumber || "-"} | Chunk {c.chunkIndex ?? "-"}
                            </span>
                            <span className="text-zinc-400 text-[11px] leading-normal italic block mt-0.5 line-clamp-2 break-all">
                              &ldquo;{c.snippetPreview || "暂无片段预览"}&rdquo;
                            </span>
                          </div>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                {/* 越界引用提示 (invalid_cleaned) */}
                {msg.role === "assistant" && msg.citationIntegrity === "invalid_cleaned" && (
                  <p className="text-[10px] text-amber-500/80 flex items-center gap-1 px-1">
                    <AlertTriangle className="w-3.5 h-3.5 text-amber-500 shrink-0" />
                    检测到大模型输出发生越界引用，引用的脚标已被安全清洗。
                  </p>
                )}
              </div>
            </div>
          ))}

          {messages.length === 1 && !loading && (
            <div className="border border-dashed border-zinc-800 rounded-2xl p-6 text-center text-zinc-500 max-w-md mx-auto my-4 space-y-2">
              <Sparkles className="w-8 h-8 text-zinc-700 mx-auto animate-pulse" />
              <p className="text-xs font-medium text-zinc-400">新建问答会话</p>
              <p className="text-[11px]">当前会话暂无历史问答记录。请在下方输入框中输入您的问题，开启基于个人知识库的深度问答。</p>
            </div>
          )}

          {loading && (
            <div className="flex gap-3 max-w-[80%] mr-auto items-center animate-pulse min-w-0">
              <div className="w-8 h-8 rounded-full bg-indigo-500/10 text-indigo-400 border border-indigo-500/20 flex items-center justify-center text-xs font-bold shrink-0">
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              </div>
              <div className="bg-zinc-900/40 border border-zinc-800/30 p-3.5 rounded-2xl rounded-tl-none text-xs text-zinc-500 min-w-0">
                正在查找相关文档并整理回答...
              </div>
            </div>
          )}

          <div ref={chatEndRef} />
        </div>

        {/* 输入框输入区 */}
        <form onSubmit={handleSend} className="p-4 border-t border-zinc-800/40 bg-zinc-900/20 flex gap-2">

        {/* 清除确认弹窗 */}
        {showClearConfirm && (
          <div className="absolute inset-0 flex items-center justify-center bg-zinc-950/80 backdrop-blur-sm rounded-2xl z-10">
            <div className="bg-zinc-900 border border-zinc-700 rounded-2xl p-6 max-w-sm mx-4 shadow-2xl animate-in fade-in zoom-in-95 duration-200">
              <h4 className="text-sm font-semibold text-white mb-2">清除当前对话记录</h4>
              <p className="text-xs text-zinc-400 leading-relaxed mb-5">
                此操作将清除当前 RAG 对话窗口中的所有历史消息。<br />
                <span className="text-zinc-500">（不会删除已上传的文档和知识库文件）</span>
              </p>
              <div className="flex gap-2 justify-end">
                <button
                  type="button"
                  onClick={() => setShowClearConfirm(false)}
                  disabled={clearing}
                  className="px-4 py-2 text-xs text-zinc-400 hover:text-white bg-zinc-800 hover:bg-zinc-700 rounded-xl transition-colors"
                >
                  取消
                </button>
                <button
                  type="button"
                  onClick={handleClearConversation}
                  disabled={clearing}
                  className="px-4 py-2 text-xs font-semibold text-white bg-red-600 hover:bg-red-700 rounded-xl transition-colors disabled:opacity-50 flex items-center gap-1.5"
                >
                  {clearing ? (
                    <><Loader2 className="w-3 h-3 animate-spin" /> 清除中...</>
                  ) : (
                    "确认清除"
                  )}
                </button>
              </div>
            </div>
          </div>
        )}

          <input
            type="text"
            value={inputValue}
            onChange={(e) => setInputValue(e.target.value)}
            disabled={loading}
            placeholder={
              docs.length === 0
                ? "请先在知识库管理中上传解析文档..."
                : "输入您关于个人知识库文档的问题，回车发送..."
            }
            className="flex-1 bg-zinc-950 border border-zinc-800/80 rounded-xl px-4 py-2.5 text-sm text-white placeholder-zinc-600 focus:outline-none focus:border-indigo-500 transition-colors disabled:opacity-50"
          />
          <button
            type="submit"
            disabled={loading || !inputValue.trim() || docs.length === 0}
            className="bg-indigo-600 hover:bg-indigo-700 text-white p-2.5 rounded-xl font-medium transition-colors disabled:opacity-30 flex items-center justify-center shrink-0"
            title="发送消息"
          >
            <Send className="w-4 h-4" />
          </button>
        </form>

      </div>
    </div>
  );
}

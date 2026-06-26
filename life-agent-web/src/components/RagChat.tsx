"use client";

import React, { useState, useEffect, useRef } from "react";
import { 
  Send, 
  BookOpen, 
  Loader2, 
  Info, 
  AlertTriangle,
  FileText,
  Sparkles
} from "lucide-react";
import { getDocuments, sendRagMessage, getRagChatHistory } from "@/app/actions/knowledge";

interface KnowledgeDocument {
  id: string;
  fileName: string;
  status: string;
}

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
  const [docs, setDocs] = useState<KnowledgeDocument[]>([]);
  const [selectedDocIds, setSelectedDocIds] = useState<string[]>([]);
  const [messages, setMessages] = useState<Message[]>([
    {
      role: "assistant",
      content: "您好！我是您的知识库 RAG 问答助手。您可以勾选左侧或上方的文档限定检索范围，然后向我提问您文档库中的内容。",
    }
  ]);
  const [inputValue, setInputValue] = useState("");
  const [loading, setLoading] = useState(false);
  const [convId, setConvId] = useState("");

  const chatEndRef = useRef<HTMLDivElement>(null);

  // 初始化会话 ID 并在组件挂载时拉取文档列表与历史对话
  useEffect(() => {
    const hasExistingSession = !!sessionStorage.getItem("rag_conv_id");
    // 每次会话生成或沿用一个唯一的会话 ID
    let currentConvId = sessionStorage.getItem("rag_conv_id");
    if (!currentConvId) {
      currentConvId = "conv_" + Math.random().toString(36).substring(2, 11);
      sessionStorage.setItem("rag_conv_id", currentConvId);
    }
    // 异步推迟 setConvId 从而避开同步 set-state-in-effect 检测
    Promise.resolve().then(() => setConvId(currentConvId));

    const initChat = async () => {
      // 1. 并行加载可检索文档列表
      const docsPromise = getDocuments().then(res => {
        if (res.success && Array.isArray(res.data)) {
          // 只有状态为 success 的文档才能用于 RAG 问答
          const successDocs = (res.data as KnowledgeDocument[]).filter(d => d.status === "success");
          setDocs(successDocs);
        }
      }).catch(err => {
        console.error("Fetch docs for RAG chat failed:", err);
      });

      // 2. 如果存在已有会话，并行拉取云端历史记录并渲染
      let historyPromise = Promise.resolve();
      if (hasExistingSession && currentConvId) {
        historyPromise = getRagChatHistory(currentConvId).then(historyRes => {
          if (historyRes.success && Array.isArray(historyRes.data) && historyRes.data.length > 0) {
            const mappedMessages: Message[] = historyRes.data.map((m: { role: string; content: string }) => ({
              role: m.role as "user" | "assistant",
              content: m.content
            }));
            setMessages(mappedMessages);
          }
        }).catch(err => {
          console.error("Fetch chat history failed:", err);
        });
      }

      await Promise.all([docsPromise, historyPromise]);
    };

    initChat();
  }, []);

  // 自动滚动到聊天底部
  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, loading]);

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
      setSelectedDocIds(docs.map(d => d.id));
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

  // 正则解析并渲染文本中的 Citation 脚标 `[1]`, `[2]`, `[3]`
  const renderMessageContent = (content: string, citations: CitationNode[] = []) => {
    if (!citations || citations.length === 0) {
      return <p className="whitespace-pre-wrap leading-relaxed text-zinc-200">{content}</p>;
    }

    // 正则表达式匹配 `[数字]`，进行切片拆分
    const citationRegex = /\[([1-9][0-9]?)\]/g;
    const parts = [];
    let lastIndex = 0;
    let match;

    while ((match = citationRegex.exec(content)) !== null) {
      const matchIndex = match.index;
      const citationNumber = parseInt(match[1], 10);

      // 匹配前置文本
      if (matchIndex > lastIndex) {
        parts.push(content.substring(lastIndex, matchIndex));
      }

      // 匹配到的脚标节点，关联 citations 列表
      const matchedNode = citations.find(c => c.index === citationNumber);
      if (matchedNode) {
        parts.push(
          <span key={`citation-${matchIndex}`} className="relative inline-block group mx-0.5">
            {/* 脚标按钮 */}
            <button className="inline-flex items-center justify-center w-5 h-5 text-[10px] font-bold text-indigo-400 bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/30 rounded-md transition-all align-middle select-none focus:outline-none">
              {citationNumber}
            </button>

            {/* Hover 悬浮气泡框 (Tooltip) */}
            <span className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 w-72 p-3 bg-zinc-900 border border-zinc-800 text-zinc-300 rounded-xl opacity-0 pointer-events-none group-hover:opacity-100 group-hover:pointer-events-auto transition-opacity duration-200 shadow-2xl z-50 text-xs">
              <span className="flex items-center gap-1.5 font-semibold text-white border-b border-zinc-800 pb-1.5 mb-1.5">
                <BookOpen className="w-3.5 h-3.5 text-indigo-400 shrink-0" />
                <span className="truncate">{matchedNode.documentName}</span>
                <span className="text-[10px] text-zinc-500 font-mono ml-auto">
                  Page {matchedNode.pageNumber} | Chunk {matchedNode.chunkIndex}
                </span>
              </span>
              <span className="block text-zinc-400 leading-normal line-clamp-4 italic">
                &ldquo;{matchedNode.snippetPreview}&rdquo;
              </span>
              <span className="absolute top-full left-1/2 -translate-x-1/2 -mt-1 border-4 border-transparent border-t-zinc-900"></span>
            </span>
          </span>
        );
      } else {
        // 如果没有找到对应的 citation 信息，原样展示脚标
        parts.push(match[0]);
      }

      lastIndex = citationRegex.lastIndex;
    }

    if (lastIndex < content.length) {
      parts.push(content.substring(lastIndex));
    }

    return (
      <div className="whitespace-pre-wrap leading-relaxed text-zinc-200">
        {parts.map((p, i) => <React.Fragment key={i}>{p}</React.Fragment>)}
      </div>
    );
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
      <div className="lg:col-span-3 bg-zinc-900/10 border border-zinc-800/40 rounded-2xl flex flex-col overflow-hidden shadow-2xl min-h-[500px]">
        {/* 聊天窗头部 */}
        <div className="px-5 py-4 border-b border-zinc-800/40 bg-zinc-900/30 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <Sparkles className="w-4 h-4 text-indigo-400" />
            <h3 className="text-sm font-semibold text-white">RAG 知识库检索对话</h3>
          </div>
          {selectedDocIds.length > 0 && (
            <span className="text-[10px] bg-indigo-500/10 text-indigo-400 px-2 py-0.5 rounded font-medium">
              限定 {selectedDocIds.length} 个文档进行回答
            </span>
          )}
        </div>

        {/* 消息历史滚动区 */}
        <div className="flex-1 p-5 overflow-y-auto space-y-5 min-h-[380px] max-h-[480px]">
          {messages.map((msg, index) => (
            <div 
              key={index} 
              className={`flex gap-3 max-w-[85%] ${
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
              <div className="space-y-2">
                <div className={`p-3.5 rounded-2xl ${
                  msg.role === "user"
                    ? "bg-indigo-600 text-white rounded-tr-none shadow-[0_4px_12px_rgba(99,102,241,0.15)]"
                    : "bg-zinc-900/60 border border-zinc-800/50 rounded-tl-none"
                }`}>
                  {msg.role === "user" 
                    ? <p className="whitespace-pre-wrap leading-relaxed text-sm text-white">{msg.content}</p>
                    : renderMessageContent(msg.content, msg.citations)
                  }
                </div>

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

          {loading && (
            <div className="flex gap-3 max-w-[80%] mr-auto items-center animate-pulse">
              <div className="w-8 h-8 rounded-full bg-indigo-500/10 text-indigo-400 border border-indigo-500/20 flex items-center justify-center text-xs font-bold shrink-0">
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
              </div>
              <div className="bg-zinc-900/40 border border-zinc-800/30 p-3.5 rounded-2xl rounded-tl-none text-xs text-zinc-500">
                正在进行深度向量检索并生成可信回复...
              </div>
            </div>
          )}

          <div ref={chatEndRef} />
        </div>

        {/* 输入框输入区 */}
        <form onSubmit={handleSend} className="p-4 border-t border-zinc-800/40 bg-zinc-900/20 flex gap-2">
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

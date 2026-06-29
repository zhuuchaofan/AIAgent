"use client";

import React, { createContext, useContext, useEffect, useState, useRef, useCallback } from "react";
import { getDocuments, uploadDocument, deleteDocument } from "@/app/actions/knowledge";
import { useAuth } from "@/providers/AuthProvider";
import { CheckCircle, XCircle, AlertCircle, X } from "lucide-react";

export interface KnowledgeDocument {
  id: string;
  userId: string;
  fileName: string;
  fileSize: number;
  mimeType: string;
  gcsPath: string;
  status: string; // processing, success, failed, deleting
  chunkCount: number;
  errorMessage: string | null;
  createdAt: string;
  updatedAt: string;
}

interface Toast {
  id: string;
  message: string;
  type: "success" | "error" | "info";
}

interface DocumentContextType {
  documents: KnowledgeDocument[];
  loading: boolean;
  fetchDocs: (silent?: boolean) => Promise<void>;
  uploadDoc: (file: File) => Promise<{ success: boolean; message?: string }>;
  deleteDoc: (docId: string) => Promise<{ success: boolean; message?: string }>;
}

const DocumentContext = createContext<DocumentContextType | undefined>(undefined);

export function DocumentProvider({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [loading, setLoading] = useState(false);
  const [toasts, setToasts] = useState<Toast[]>([]);

  const pollingRef = useRef<NodeJS.Timeout | null>(null);
  const uploadRefreshTimersRef = useRef<NodeJS.Timeout[]>([]);
  const shownToastsRef = useRef<Set<string>>(new Set());

  // 弹窗队列控制
  const addToast = useCallback((message: string, type: "success" | "error" | "info") => {
    const id = Math.random().toString(36).substring(2, 9);
    setToasts(prev => {
      const newToast: Toast = { id, message, type };
      const nextToasts = [...prev, newToast];
      if (nextToasts.length > 3) {
        return nextToasts.slice(nextToasts.length - 3);
      }
      return nextToasts;
    });

    setTimeout(() => {
      setToasts(prev => prev.filter(t => t.id !== id));
    }, 5000);
  }, []);

  // 跨轮询文档状态变更检测与 Toast 去重触发
  const checkStatusChanges = useCallback((prev: KnowledgeDocument[], next: KnowledgeDocument[]) => {
    if (prev.length === 0) return; // 忽略首次加载，防止刷历史弹窗

    next.forEach(nextDoc => {
      const prevDoc = prev.find(p => p.id === nextDoc.id);
      if (!prevDoc) return;

      if (prevDoc.status === "processing") {
        if (nextDoc.status === "success") {
          const key = `${nextDoc.id}-success`;
          if (!shownToastsRef.current.has(key)) {
            shownToastsRef.current.add(key);
            addToast(`🎉 文档《${nextDoc.fileName}》解析完成，现已可用于问答检索。`, "success");
          }
        } else if (nextDoc.status === "failed") {
          const key = `${nextDoc.id}-failed`;
          if (!shownToastsRef.current.has(key)) {
            shownToastsRef.current.add(key);
            addToast(`❌ 文档《${nextDoc.fileName}》解析失败：${nextDoc.errorMessage || "未知错误"}`, "error");
          }
        }
      }
    });

    // 检查删除成功状态
    prev.forEach(prevDoc => {
      if (prevDoc.status === "deleting") {
        const exists = next.some(n => n.id === prevDoc.id);
        if (!exists) {
          const key = `${prevDoc.id}-deleted`;
          if (!shownToastsRef.current.has(key)) {
            shownToastsRef.current.add(key);
            addToast(`🗑️ 文档《${prevDoc.fileName}》已从知识库中移除。`, "success");
          }
        }
      }
    });
  }, [addToast]);

  // 拉取文档逻辑
  const fetchDocs = useCallback(async (silent = false) => {
    if (!user) return;
    if (!silent) {
      setLoading(true);
    }
    try {
      const res = await getDocuments();
      if (res.success && Array.isArray(res.data)) {
        setDocuments(prev => {
          checkStatusChanges(prev, res.data);
          return res.data;
        });
      }
    } catch (err) {
      console.error("Fetch documents failed in provider:", err);
    } finally {
      if (!silent) {
        setLoading(false);
      }
    }
  }, [user, checkStatusChanges]);

  // 上传逻辑封装
  const uploadDoc = useCallback(async (file: File) => {
    const formData = new FormData();
    formData.append("file", file);
    try {
      const res = await uploadDocument(formData);
      if (res.success) {
        await fetchDocs(true);

        const timer = setTimeout(() => {
          void fetchDocs(true);
          uploadRefreshTimersRef.current = uploadRefreshTimersRef.current.filter(t => t !== timer);
        }, 1200);
        uploadRefreshTimersRef.current.push(timer);
      }
      return res;
    } catch (err) {
      console.error("Upload document failed in provider:", err);
      throw err;
    }
  }, [fetchDocs]);

  // 删除逻辑封装
  const deleteDoc = useCallback(async (docId: string) => {
    setDocuments(prev => 
      prev.map(doc => doc.id === docId ? { ...doc, status: "deleting" } : doc)
    );
    try {
      const res = await deleteDocument(docId);
      if (res.success) {
        fetchDocs(true);
      } else {
        fetchDocs(true);
      }
      return res;
    } catch (err) {
      console.error("Delete document failed in provider:", err);
      fetchDocs(true);
      throw err;
    }
  }, [fetchDocs]);

  // 轮询控制
  const startPolling = useCallback(() => {
    if (pollingRef.current) return;
    pollingRef.current = setInterval(() => {
      fetchDocs(true);
    }, 3000);
  }, [fetchDocs]);

  const stopPolling = useCallback(() => {
    if (pollingRef.current) {
      clearInterval(pollingRef.current);
      pollingRef.current = null;
    }
  }, []);

  // 监听文档列表激活状态触发轮询
  useEffect(() => {
    if (!user) {
      stopPolling();
      return;
    }

    const hasActiveStates = documents.some(
      doc => doc.status === "processing" || doc.status === "deleting"
    );

    const isVisible = typeof document !== "undefined" && document.visibilityState === "visible";

    if (hasActiveStates && isVisible) {
      startPolling();
    } else {
      stopPolling();
    }
  }, [documents, user, startPolling, stopPolling]);

  // VisibilityChange 事件监听，锁屏或隐藏挂起轮询，重回前台立即刷新
  useEffect(() => {
    if (!user) return;

    const handleVisibilityChange = () => {
      const isVisible = document.visibilityState === "visible";
      if (isVisible) {
        fetchDocs(true);
      } else {
        stopPolling();
      }
    };

    document.addEventListener("visibilitychange", handleVisibilityChange);
    return () => {
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, [user, fetchDocs, stopPolling]);

  // 登录状态边界控制：登入/登出切换重置所有状态和 Timer
  useEffect(() => {
    const handleUserChange = async () => {
      if (user) {
        shownToastsRef.current.clear();
        await Promise.resolve();
        setDocuments([]);
        setLoading(true);
        fetchDocs(false);
      } else {
        stopPolling();
        await Promise.resolve();
        setDocuments([]);
        setLoading(false);
        setToasts([]);
        shownToastsRef.current.clear();
      }
    };
    handleUserChange();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [user]);

  // 销毁时彻底清理
  useEffect(() => {
    return () => {
      stopPolling();
      uploadRefreshTimersRef.current.forEach(timer => clearTimeout(timer));
      uploadRefreshTimersRef.current = [];
    };
  }, [stopPolling]);

  return (
    <DocumentContext.Provider value={{ documents, loading, fetchDocs, uploadDoc, deleteDoc }}>
      {children}
      {/* 全局暗色高对比度 Toast 提示容器：放置于顶部中央，避免遮挡移动端底栏输入框 */}
      <div className="fixed top-6 left-1/2 -translate-x-1/2 z-50 flex flex-col gap-2.5 w-full max-w-sm px-4 pointer-events-none">
        {toasts.map(toast => (
          <div
            key={toast.id}
            className="pointer-events-auto flex items-start gap-3 p-4 rounded-2xl border backdrop-blur-md bg-zinc-900/95 border-zinc-800 shadow-2xl transition-all duration-300 animate-in slide-in-from-top-5 max-w-full"
          >
            {toast.type === "success" && (
              <CheckCircle className="w-5 h-5 shrink-0 text-emerald-400 mt-0.5" />
            )}
            {toast.type === "error" && (
              <XCircle className="w-5 h-5 shrink-0 text-red-400 mt-0.5" />
            )}
            {toast.type === "info" && (
              <AlertCircle className="w-5 h-5 shrink-0 text-indigo-400 mt-0.5" />
            )}
            <div className="text-xs font-medium text-zinc-300 flex-1 leading-relaxed break-words pr-2">
              {toast.message}
            </div>
            <button
              onClick={() => setToasts(prev => prev.filter(t => t.id !== toast.id))}
              className="text-zinc-500 hover:text-zinc-300 shrink-0 hover:bg-zinc-800/40 p-1 rounded transition-colors"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        ))}
      </div>
    </DocumentContext.Provider>
  );
}

export function useDocuments() {
  const context = useContext(DocumentContext);
  if (context === undefined) {
    throw new Error("useDocuments must be used within a DocumentProvider");
  }
  return context;
}

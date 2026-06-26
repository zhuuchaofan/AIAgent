"use client";

import React, { useState, useEffect, useRef } from "react";
import { 
  Upload, 
  Trash2, 
  Loader2, 
  CheckCircle, 
  XCircle, 
  AlertCircle, 
  FileText,
  RefreshCw
} from "lucide-react";
import { getDocuments, uploadDocument, deleteDocument } from "@/app/actions/knowledge";

interface KnowledgeDocument {
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

export function KnowledgeBase() {
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);
  const [loading, setLoading] = useState(true);
  const [uploading, setUploading] = useState(false);
  const [dragActive, setDragActive] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  
  const fileInputRef = useRef<HTMLInputElement>(null);
  const pollTimerRef = useRef<NodeJS.Timeout | null>(null);

  // 轮询管理助手方法（移动到 Effect 之前声明以符合使用前声明规则）
  const startPolling = () => {
    if (pollTimerRef.current) return;
    pollTimerRef.current = setInterval(() => {
      fetchDocs(true);
    }, 3000); // 每 3 秒轮询一次
  };

  const stopPolling = () => {
    if (pollTimerRef.current) {
      clearInterval(pollTimerRef.current);
      pollTimerRef.current = null;
    }
  };

  // 获取文档列表
  const fetchDocs = async (silent = false) => {
    if (!silent) {
      // 避免同步在 Effect 核心调度内调用 setState
      await Promise.resolve();
      setLoading(true);
    }
    try {
      const res = await getDocuments();
      if (res.success && Array.isArray(res.data)) {
        setDocuments(res.data);
      }
    } catch (err: unknown) {
      console.error("Fetch documents failed:", err);
      const errMsg = err instanceof Error ? err.message : String(err);
      setError("获取文档列表失败：" + errMsg);
    } finally {
      if (!silent) setLoading(false);
    }
  };

  // 初始化获取
  useEffect(() => {
    fetchDocs();
    return () => stopPolling();
  }, []);

  // 轮询管理：检查是否有文档处于 processing 或 deleting 状态
  useEffect(() => {
    const hasActiveStates = documents.some(
      doc => doc.status === "processing" || doc.status === "deleting"
    );

    if (hasActiveStates) {
      startPolling();
    } else {
      stopPolling();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [documents]);

  // 处理拖拽
  const handleDrag = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    if (e.type === "dragenter" || e.type === "dragover") {
      setDragActive(true);
    } else if (e.type === "dragleave") {
      setDragActive(false);
    }
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
    
    if (e.dataTransfer.files && e.dataTransfer.files[0]) {
      await handleFileSelection(e.dataTransfer.files[0]);
    }
  };

  const handleFileInputChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      await handleFileSelection(e.target.files[0]);
    }
  };

  // 上传文件校验与执行
  const handleFileSelection = async (file: File) => {
    setError(null);
    setSuccessMsg(null);

    // 格式校验
    const allowedExtensions = [".pdf", ".txt", ".md"];
    const fileNameLower = file.name.toLowerCase();
    const isAllowed = allowedExtensions.some(ext => fileNameLower.endsWith(ext));
    if (!isAllowed) {
      setError("不支持的文件格式。仅允许上传 PDF、TXT、MD 文件。");
      return;
    }

    // 大小校验 (最大 10MB)
    const maxSize = 10 * 1024 * 1024;
    if (file.size > maxSize) {
      setError("文件大小超出限制。最大允许上传 10MB。");
      return;
    }

    setUploading(true);
    const formData = new FormData();
    formData.append("file", file);

    try {
      const res = await uploadDocument(formData);
      if (res.success) {
        setSuccessMsg(`文件「${file.name}」已提交上传，正在解析切片中...`);
        fetchDocs(true);
      }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setError("上传失败：" + errMsg);
    } finally {
      setUploading(false);
      if (fileInputRef.current) {
        fileInputRef.current.value = "";
      }
    }
  };

  // 删除文件
  const handleDelete = async (docId: string) => {
    setConfirmDeleteId(null);
    setError(null);
    setSuccessMsg(null);

    // 先在前端更新文档状态为 deleting
    setDocuments(prev => 
      prev.map(doc => doc.id === docId ? { ...doc, status: "deleting" } : doc)
    );

    try {
      const res = await deleteDocument(docId);
      if (res.success) {
        setSuccessMsg("文档已成功删除。");
        fetchDocs(true);
      }
    } catch (err: unknown) {
      const errMsg = err instanceof Error ? err.message : String(err);
      setError("删除文档失败：" + errMsg);
      fetchDocs(true);
    }
  };

  const formatBytes = (bytes: number, decimals = 2) => {
    if (bytes === 0) return "0 Bytes";
    const k = 1024;
    const dm = decimals < 0 ? 0 : decimals;
    const sizes = ["Bytes", "KB", "MB", "GB"];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(dm)) + " " + sizes[i];
  };

  const formatDate = (dateStr: string) => {
    try {
      const d = new Date(dateStr);
      return d.toLocaleString("zh-CN", { 
        year: "numeric", 
        month: "2-digit", 
        day: "2-digit", 
        hour: "2-digit", 
        minute: "2-digit" 
      });
    } catch {
      return dateStr;
    }
  };

  return (
    <div className="space-y-8 animate-in fade-in slide-in-from-bottom-3 duration-500">
      {/* 头部信息 */}
      <div className="flex justify-between items-center border-b border-zinc-800/40 pb-4">
        <div>
          <h2 className="text-xl font-semibold text-white">个人知识库管理</h2>
          <p className="text-zinc-500 text-xs mt-1">上传 PDF / TXT / MD 文件，自动进行向量解析以支持精准 RAG 问答。</p>
        </div>
        <button 
          onClick={() => fetchDocs(false)} 
          disabled={loading || uploading}
          className="p-2 hover:bg-zinc-800/50 rounded-lg text-zinc-400 hover:text-white transition-colors disabled:opacity-50"
          title="刷新列表"
        >
          <RefreshCw className={`w-4 h-4 ${loading ? "animate-spin text-indigo-400" : ""}`} />
        </button>
      </div>

      {/* 错误 & 成功提示 */}
      {error && (
        <div className="bg-red-500/10 border border-red-500/30 text-red-400 p-4 rounded-xl flex items-start gap-3 animate-in fade-in duration-300">
          <AlertCircle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="text-sm">{error}</div>
        </div>
      )}

      {successMsg && (
        <div className="bg-emerald-500/10 border border-emerald-500/30 text-emerald-400 p-4 rounded-xl flex items-start gap-3 animate-in fade-in duration-300">
          <CheckCircle className="w-5 h-5 shrink-0 mt-0.5" />
          <div className="text-sm">{successMsg}</div>
        </div>
      )}

      {/* 拖拽上传组件 */}
      <div
        onDragEnter={handleDrag}
        onDragOver={handleDrag}
        onDragLeave={handleDrag}
        onDrop={handleDrop}
        onClick={() => fileInputRef.current?.click()}
        className={`border-2 border-dashed rounded-2xl p-8 text-center cursor-pointer transition-all duration-300 flex flex-col items-center justify-center min-h-[180px] bg-zinc-900/20 ${
          dragActive 
            ? "border-indigo-500 bg-indigo-500/5 shadow-[0_0_15px_rgba(99,102,241,0.1)]" 
            : "border-zinc-800 hover:border-zinc-700 hover:bg-zinc-900/40"
        } ${uploading ? "pointer-events-none opacity-60" : ""}`}
      >
        <input
          ref={fileInputRef}
          type="file"
          className="hidden"
          accept=".pdf,.txt,.md"
          onChange={handleFileInputChange}
          disabled={uploading}
        />
        
        {uploading ? (
          <>
            <Loader2 className="w-10 h-10 animate-spin text-indigo-500 mb-4" />
            <p className="text-sm font-medium text-white">正在上传并提交解析请求...</p>
            <p className="text-xs text-zinc-500 mt-1">请勿刷新或关闭页面</p>
          </>
        ) : (
          <>
            <div className="w-12 h-12 rounded-xl bg-zinc-800/50 flex items-center justify-center mb-4 text-zinc-400 group-hover:text-white transition-colors">
              <Upload className="w-6 h-6" />
            </div>
            <p className="text-sm font-medium text-zinc-300">
              <span className="text-indigo-400 font-semibold">点击选择文件</span> 或拖拽到此处
            </p>
            <p className="text-xs text-zinc-500 mt-2">支持 PDF, TXT, MD 文件，大小限制为 10MB 内</p>
          </>
        )}
      </div>

      {/* 文档列表展示 */}
      <div className="bg-zinc-900/30 border border-zinc-800/50 rounded-2xl overflow-hidden shadow-xl">
        <div className="p-4 border-b border-zinc-800/50 bg-zinc-900/40 flex items-center justify-between">
          <h3 className="text-sm font-semibold text-zinc-300">已上传知识库文档 ({documents.length})</h3>
        </div>

        {loading && documents.length === 0 ? (
          <div className="py-12 flex flex-col items-center justify-center text-zinc-500">
            <Loader2 className="w-8 h-8 animate-spin text-indigo-500 mb-3" />
            <span className="text-sm">加载文档元数据...</span>
          </div>
        ) : documents.length === 0 ? (
          <div className="py-12 text-center text-zinc-500 flex flex-col items-center justify-center">
            <FileText className="w-12 h-12 text-zinc-700 mb-3" />
            <p className="text-sm">知识库暂无文档</p>
            <p className="text-xs text-zinc-600 mt-1">在上方上传第一个文件以丰富您的知识库。</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm border-collapse">
              <thead>
                <tr className="border-b border-zinc-800/40 text-xs text-zinc-500 uppercase tracking-wider bg-zinc-900/20">
                  <th className="py-3.5 px-4 font-semibold">文件名</th>
                  <th className="py-3.5 px-4 font-semibold">文件大小</th>
                  <th className="py-3.5 px-4 font-semibold">分块数</th>
                  <th className="py-3.5 px-4 font-semibold">上传时间</th>
                  <th className="py-3.5 px-4 font-semibold">当前状态</th>
                  <th className="py-3.5 px-4 font-semibold text-right">操作</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-zinc-800/30">
                {documents.map((doc) => (
                  <tr key={doc.id} className="hover:bg-zinc-800/20 transition-colors group">
                    {/* 文件名 */}
                    <td className="py-4 px-4 font-medium text-zinc-200">
                      <div className="flex items-center gap-3">
                        <FileText className="w-4 h-4 text-indigo-400 shrink-0" />
                        <span className="truncate max-w-[200px] md:max-w-[300px]" title={doc.fileName}>
                          {doc.fileName}
                        </span>
                      </div>
                    </td>

                    {/* 文件大小 */}
                    <td className="py-4 px-4 text-zinc-400">
                      {formatBytes(doc.fileSize)}
                    </td>

                    {/* 分块数 */}
                    <td className="py-4 px-4 text-zinc-400 font-mono">
                      {doc.chunkCount > 0 ? (
                        <span className="bg-indigo-500/10 text-indigo-400 px-2 py-0.5 rounded text-xs font-semibold">
                          {doc.chunkCount} Chunks
                        </span>
                      ) : (
                        <span className="text-zinc-600">-</span>
                      )}
                    </td>

                    {/* 创建时间 */}
                    <td className="py-4 px-4 text-zinc-400">
                      {formatDate(doc.createdAt)}
                    </td>

                    {/* 当前状态 */}
                    <td className="py-4 px-4">
                      {doc.status === "processing" && (
                        <span className="inline-flex items-center gap-1.5 text-xs text-amber-400 bg-amber-500/10 px-2.5 py-1 rounded-full font-medium">
                          <Loader2 className="w-3.5 h-3.5 animate-spin" />
                          解析中
                        </span>
                      )}
                      {doc.status === "deleting" && (
                        <span className="inline-flex items-center gap-1.5 text-xs text-zinc-400 bg-zinc-500/10 px-2.5 py-1 rounded-full font-medium">
                          <Loader2 className="w-3.5 h-3.5 animate-spin" />
                          删除中
                        </span>
                      )}
                      {doc.status === "success" && (
                        <span className="inline-flex items-center gap-1.5 text-xs text-emerald-400 bg-emerald-500/10 px-2.5 py-1 rounded-full font-medium">
                          <CheckCircle className="w-3.5 h-3.5" />
                          成功
                        </span>
                      )}
                      {doc.status === "failed" && (
                        <div className="flex flex-col gap-1 items-start">
                          <span className="inline-flex items-center gap-1.5 text-xs text-red-400 bg-red-500/10 px-2.5 py-1 rounded-full font-medium">
                            <XCircle className="w-3.5 h-3.5" />
                            失败
                          </span>
                          {doc.errorMessage && (
                            <span 
                              className="text-[10px] text-red-400/80 max-w-[150px] truncate block" 
                              title={doc.errorMessage}
                            >
                              {doc.errorMessage}
                            </span>
                          )}
                        </div>
                      )}
                    </td>

                    {/* 操作 */}
                    <td className="py-4 px-4 text-right">
                      {confirmDeleteId === doc.id ? (
                        <div className="flex items-center justify-end gap-2 animate-in slide-in-from-right-2 duration-200">
                          <button
                            onClick={() => handleDelete(doc.id)}
                            className="bg-red-600 hover:bg-red-700 text-white text-xs px-2.5 py-1 rounded-lg font-medium transition-colors"
                          >
                            确定
                          </button>
                          <button
                            onClick={() => setConfirmDeleteId(null)}
                            className="bg-zinc-800 hover:bg-zinc-700 text-zinc-400 hover:text-white text-xs px-2.5 py-1 rounded-lg font-medium transition-colors"
                          >
                            取消
                          </button>
                        </div>
                      ) : (
                        <button
                          onClick={() => setConfirmDeleteId(doc.id)}
                          disabled={doc.status === "deleting" || doc.status === "processing"}
                          className="p-1.5 hover:bg-red-500/10 text-zinc-500 hover:text-red-400 rounded-lg transition-colors disabled:opacity-30 disabled:hover:bg-transparent"
                          title="删除文档"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

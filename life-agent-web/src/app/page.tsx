"use client";

import { useEffect, useState } from "react";
import { useAuth } from "@/providers/AuthProvider";
import { IngestForm } from "@/components/IngestForm";
import { Timeline } from "@/components/Timeline";
import { ReminderWidget } from "@/components/ReminderWidget";
import { DailySummaryCard } from "@/components/DailySummaryCard";
import { Loader2 } from "lucide-react";
import { KnowledgeBase } from "@/components/KnowledgeBase";
import { RagChat } from "@/components/RagChat";
import { AgentPreview } from "@/components/AgentPreview";
import { getFeatureFlags } from "@/app/actions/config";

export default function Home() {
  const { user, loading, loginWithGoogle, logoutUser } = useAuth();
  const [refreshTrigger, setRefreshTrigger] = useState(0);
  const [activeTab, setActiveTab] = useState<"assistant" | "knowledge" | "chat">("assistant");
  const [showAgentPreview, setShowAgentPreview] = useState(false);

  useEffect(() => {
    let cancelled = false;

    getFeatureFlags()
      .then(flags => {
        if (!cancelled) {
          setShowAgentPreview(flags.agentPreviewEnabled);
        }
      })
      .catch(error => {
        console.error("Failed to load feature flags:", error);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const handleLogin = async () => {
    try {
      await loginWithGoogle();
    } catch (error) {
      console.error("Login failed:", error);
    }
  };

  const handleLogout = async () => {
    try {
      await logoutUser();
    } catch (error) {
      console.error("Logout failed:", error);
    }
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-zinc-950 flex items-center justify-center">
        <Loader2 className="w-8 h-8 animate-spin text-zinc-500" />
      </div>
    );
  }

  const isLoggedIn = !!user;

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-300 p-6 md:p-12 font-sans selection:bg-indigo-500/30">
      <div className="max-w-6xl mx-auto">
        <header className="flex justify-between items-center mb-8 border-b border-zinc-800/50 pb-6">
          <div>
            <h1 className="text-2xl font-bold bg-clip-text text-transparent bg-gradient-to-r from-indigo-400 to-cyan-400">
              LifeOS Personal Home
            </h1>
            <p className="text-zinc-500 text-sm mt-1">个人助手、生活记录、提醒、每日总结与知识库</p>
          </div>
          
          {isLoggedIn ? (
            <button onClick={handleLogout} className="text-sm text-zinc-400 hover:text-white transition-colors">
              退出登录
            </button>
          ) : null}
        </header>

        {!isLoggedIn ? (
          <div className="flex flex-col items-center justify-center py-20 text-center">
            <div className="w-16 h-16 bg-indigo-500/10 rounded-2xl flex items-center justify-center mb-6">
              <svg className="w-8 h-8 text-indigo-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
              </svg>
            </div>
            <h2 className="text-2xl font-semibold text-white mb-2">欢迎回来</h2>
            <p className="text-zinc-500 mb-8 max-w-sm">安全登录以记录您的生活并回顾生活记录。</p>
            <button
              onClick={handleLogin}
              className="bg-white hover:bg-zinc-100 text-zinc-900 px-6 py-3 rounded-xl font-medium transition-colors"
            >
              使用 Google 登录
            </button>
            <p className="text-xs text-zinc-600 mt-4">
              （在未配置 Firebase 的开发模式下，此按钮作为模拟登录使用）
            </p>
          </div>
        ) : (
          <div className="space-y-6">
            {/* Tab 导航 — 桌面端可见 */}
            <div className="hidden lg:flex border-b border-zinc-800/40 pb-px mb-8 gap-6 text-sm font-semibold select-none">
              <button
                onClick={() => setActiveTab("assistant")}
                className={`pb-4 transition-all duration-300 relative ${
                  activeTab === "assistant"
                    ? "text-white"
                    : "text-zinc-500 hover:text-zinc-300"
                }`}
              >
                个人助手
                {activeTab === "assistant" && (
                  <span className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-indigo-500 to-cyan-400 rounded-full animate-in fade-in duration-300"></span>
                )}
              </button>
              <button
                onClick={() => setActiveTab("knowledge")}
                className={`pb-4 transition-all duration-300 relative ${
                  activeTab === "knowledge"
                    ? "text-white"
                    : "text-zinc-500 hover:text-zinc-300"
                }`}
              >
                知识库管理
                {activeTab === "knowledge" && (
                  <span className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-indigo-500 to-cyan-400 rounded-full animate-in fade-in duration-300"></span>
                )}
              </button>
              <button
                onClick={() => setActiveTab("chat")}
                className={`pb-4 transition-all duration-300 relative ${
                  activeTab === "chat"
                    ? "text-white"
                    : "text-zinc-500 hover:text-zinc-300"
                }`}
              >
                知识库问答 (RAG)
                {activeTab === "chat" && (
                  <span className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-indigo-500 to-cyan-400 rounded-full animate-in fade-in duration-300"></span>
                )}
              </button>
            </div>

            {/* 桌面端：Tab 切换内容区 */}
            <div className="hidden lg:contents">
              {activeTab === "assistant" && (
                <div className="animate-in fade-in duration-500 grid grid-cols-1 lg:grid-cols-3 gap-8">
                  <div className="lg:col-span-2 space-y-6">
                    {showAgentPreview && <AgentPreview />}
                    <IngestForm onIngested={() => setRefreshTrigger(t => t + 1)} />
                    <Timeline refreshTrigger={refreshTrigger} />
                  </div>
                  <div className="lg:col-span-1 space-y-6">
                    <ReminderWidget refreshTrigger={refreshTrigger} onUpdated={() => setRefreshTrigger(t => t + 1)} />
                    <DailySummaryCard refreshTrigger={refreshTrigger} />
                  </div>
                </div>
              )}

              {activeTab === "knowledge" && (
                <KnowledgeBase />
              )}

              {activeTab === "chat" && (
                <div className="space-y-6">
                  <RagChat />
                </div>
              )}
            </div>

            {/* 窄屏端：Tab 导航 — 仅在 lg:hidden 下可见 */}
            <div className="lg:hidden flex border-b border-zinc-800/40 pb-px mb-6 gap-2 text-sm font-semibold select-none">
              <button
                onClick={() => setActiveTab("assistant")}
                className={`flex-1 pb-3 text-center transition-all duration-300 relative ${
                  activeTab === "assistant"
                    ? "text-white"
                    : "text-zinc-500 hover:text-zinc-300"
                }`}
              >
                个人助手
                {activeTab === "assistant" && (
                  <span className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-indigo-500 to-cyan-400 rounded-full animate-in fade-in duration-300"></span>
                )}
              </button>
              <button
                onClick={() => setActiveTab("knowledge")}
                className={`flex-1 pb-3 text-center transition-all duration-300 relative ${
                  activeTab === "knowledge"
                    ? "text-white"
                    : "text-zinc-500 hover:text-zinc-300"
                }`}
              >
                知识库
                {activeTab === "knowledge" && (
                  <span className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-indigo-500 to-cyan-400 rounded-full animate-in fade-in duration-300"></span>
                )}
              </button>
              <button
                onClick={() => setActiveTab("chat")}
                className={`flex-1 pb-3 text-center transition-all duration-300 relative ${
                  activeTab === "chat"
                    ? "text-white"
                    : "text-zinc-500 hover:text-zinc-300"
                }`}
              >
                问答
                {activeTab === "chat" && (
                  <span className="absolute bottom-0 left-0 right-0 h-[2px] bg-gradient-to-r from-indigo-500 to-cyan-400 rounded-full animate-in fade-in duration-300"></span>
                )}
              </button>
            </div>

            {/* 窄屏端：Tab 切换内容区 */}
            <div className="lg:hidden">
              {activeTab === "assistant" && (
                <div className="space-y-6 animate-in fade-in duration-500">
                  {showAgentPreview && <AgentPreview />}
                  <IngestForm onIngested={() => setRefreshTrigger(t => t + 1)} />
                  <ReminderWidget refreshTrigger={refreshTrigger} onUpdated={() => setRefreshTrigger(t => t + 1)} />
                  <DailySummaryCard refreshTrigger={refreshTrigger} />
                  <Timeline refreshTrigger={refreshTrigger} />
                </div>
              )}

              {activeTab === "knowledge" && (
                <div className="animate-in fade-in duration-500">
                  <KnowledgeBase />
                </div>
              )}

              {activeTab === "chat" && (
                <div className="animate-in fade-in duration-500 space-y-6">
                  <RagChat />
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </main>
  );
}

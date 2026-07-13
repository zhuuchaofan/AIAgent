"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { AlertTriangle, Loader2, RefreshCw, ShieldCheck } from "lucide-react";
import { listPhase80PendingActions } from "@/app/actions/knowledge";
import { useAuth } from "@/providers/AuthProvider";

interface Phase80PendingAction {
  actionId: string;
  status: string;
  title: string;
  summary: string;
  actionType: string;
  intent?: string;
  disposition?: string;
  riskLevel?: string;
  requiresPendingAction?: boolean;
  routeReason?: string;
  intentClassifier?: string;
  createdAt: string;
  updatedAt?: string;
  expiresAt: string;
  confirmedAt?: string | null;
  cancelledAt?: string | null;
  executed: boolean;
  wroteData: boolean;
  executionReady: boolean;
  guardDecision: string;
  safetyMode: string;
  legacyConfirmEndpointUsed: boolean;
  realWritePath: boolean;
  isArchived?: boolean;
  confirmTarget?: string;
  confirmWriteEnabled?: boolean;
  confirmWriteExecutionReady?: boolean;
  confirmWriteRealPathReady?: boolean;
  confirmWriteExecutorId?: string;
  confirmWriteDecisionReason?: string;
  memoryCandidateOnly?: boolean;
  confirmPlanReason?: string;
  memoryTarget?: string;
  memoryWriteEnabled?: boolean;
  memoryRequiresDedupe?: boolean;
  memoryRequiresMerge?: boolean;
  memoryRequiresConfirmation?: boolean;
  message: string;
}

interface Phase80PersistenceMetadata {
  storeMode: string;
  firestorePersistenceEnabled: boolean;
  previewOnly: boolean;
  safetyMode: string;
}

interface Phase80ListResponse {
  success: boolean;
  message?: string;
  data?: Phase80PendingAction[];
  persistence?: Phase80PersistenceMetadata;
}

function BoolPill({ value }: { value: boolean | undefined }) {
  return (
    <span className={`rounded px-2 py-0.5 font-mono text-[11px] ${
      value
        ? "border border-emerald-500/30 bg-emerald-500/10 text-emerald-300"
        : "border border-zinc-700 bg-zinc-900 text-zinc-400"
    }`}>
      {String(value ?? false)}
    </span>
  );
}

function Field({ label, value }: { label: string; value: unknown }) {
  return (
    <div className="min-w-0 rounded-lg border border-zinc-800 bg-zinc-950/50 px-3 py-2">
      <div className="text-[10px] uppercase tracking-wide text-zinc-600">{label}</div>
      <div className="mt-1 break-words font-mono text-xs text-zinc-300">{String(value ?? "-")}</div>
    </div>
  );
}

export default function DebugPage() {
  const { user, loading, loginWithGoogle } = useAuth();
  const [actions, setActions] = useState<Phase80PendingAction[]>([]);
  const [persistence, setPersistence] = useState<Phase80PersistenceMetadata | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadDiagnostics = useCallback(async () => {
    if (!user) return;

    setIsLoading(true);
    setError(null);
    try {
      const response = await listPhase80PendingActions() as Phase80ListResponse;
      if (!response.success) {
        setError(response.message || "无法读取诊断信息。");
        setActions([]);
        setPersistence(null);
        return;
      }

      setActions(response.data ?? []);
      setPersistence(response.persistence ?? null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "无法读取诊断信息。");
      setActions([]);
      setPersistence(null);
    } finally {
      setIsLoading(false);
    }
  }, [user]);

  useEffect(() => {
    if (loading) return;

    const timer = window.setTimeout(() => {
      void loadDiagnostics();
    }, 0);

    return () => window.clearTimeout(timer);
  }, [loading, loadDiagnostics]);

  const counts = useMemo(() => {
    return actions.reduce<Record<string, number>>((acc, action) => {
      acc[action.status] = (acc[action.status] ?? 0) + 1;
      return acc;
    }, {});
  }, [actions]);

  if (loading) {
    return (
      <main className="min-h-screen bg-zinc-950 text-zinc-300 flex items-center justify-center">
        <Loader2 className="h-8 w-8 animate-spin text-zinc-500" />
      </main>
    );
  }

  if (!user) {
    return (
      <main className="min-h-screen bg-zinc-950 text-zinc-300 p-6 md:p-12">
        <div className="mx-auto max-w-xl rounded-2xl border border-zinc-800 bg-zinc-900/50 p-6">
          <h1 className="text-xl font-semibold text-zinc-100">LifeOS Debug</h1>
          <p className="mt-2 text-sm text-zinc-500">请先登录，再查看诊断信息。</p>
          <button
            type="button"
            onClick={loginWithGoogle}
            className="mt-5 rounded-xl bg-white px-4 py-2 text-sm font-medium text-zinc-900 hover:bg-zinc-100"
          >
            使用 Google 登录
          </button>
        </div>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-300 p-5 md:p-10">
      <div className="mx-auto max-w-6xl space-y-6">
        <header className="flex flex-col gap-4 border-b border-zinc-800 pb-5 md:flex-row md:items-center md:justify-between">
          <div>
            <h1 className="text-2xl font-semibold text-zinc-100">LifeOS Debug</h1>
            <p className="mt-1 text-sm text-zinc-500">Pending action 诊断视图。这里显示首页隐藏的安全与执行字段。</p>
          </div>
          <button
            type="button"
            onClick={loadDiagnostics}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-xl border border-zinc-800 bg-zinc-900 px-4 py-2 text-sm text-zinc-200 hover:bg-zinc-800 disabled:opacity-50"
          >
            {isLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <RefreshCw className="h-4 w-4" />}
            刷新
          </button>
        </header>

        {error && (
          <div className="flex items-start gap-2 rounded-xl border border-red-500/30 bg-red-500/10 p-4 text-sm text-red-200">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <span>{error}</span>
          </div>
        )}

        <section className="grid grid-cols-1 gap-3 md:grid-cols-4">
          <Field label="total" value={actions.length} />
          <Field label="pending" value={counts.pending ?? 0} />
          <Field label="confirmed" value={counts.confirmed ?? 0} />
          <Field label="cancelled / expired" value={(counts.cancelled ?? 0) + (counts.expired ?? 0)} />
        </section>

        <section className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-4">
          <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-zinc-100">
            <ShieldCheck className="h-4 w-4 text-emerald-400" />
            Persistence
          </div>
          <div className="grid grid-cols-1 gap-3 md:grid-cols-4">
            <Field label="storeMode" value={persistence?.storeMode} />
            <Field label="firestorePersistenceEnabled" value={persistence?.firestorePersistenceEnabled} />
            <Field label="previewOnly" value={persistence?.previewOnly} />
            <Field label="safetyMode" value={persistence?.safetyMode} />
          </div>
        </section>

        {isLoading ? (
          <div className="flex justify-center py-12">
            <Loader2 className="h-7 w-7 animate-spin text-zinc-500" />
          </div>
        ) : actions.length === 0 ? (
          <div className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-6 text-sm text-zinc-500">
            暂无 pending action 诊断数据。
          </div>
        ) : (
          <section className="space-y-3">
            <h2 className="text-sm font-semibold text-zinc-200">Actions</h2>
            {actions.map((action) => (
              <article key={action.actionId} className="rounded-2xl border border-zinc-800 bg-zinc-900/30 p-4">
                <div className="flex flex-col gap-2 md:flex-row md:items-start md:justify-between">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="rounded border border-zinc-700 bg-zinc-950 px-2 py-0.5 font-mono text-xs text-zinc-300">{action.status}</span>
                      <span className="rounded border border-cyan-500/30 bg-cyan-500/10 px-2 py-0.5 text-xs text-cyan-200">{action.actionType}</span>
                    </div>
                    <h3 className="mt-3 break-words text-base font-medium text-zinc-100">{action.title}</h3>
                    {action.summary && action.summary !== action.title && (
                      <p className="mt-2 break-words text-sm text-zinc-500">{action.summary}</p>
                    )}
                  </div>
                  <div className="font-mono text-[11px] text-zinc-600">{action.actionId}</div>
                </div>

                <div className="mt-4 grid grid-cols-2 gap-2 md:grid-cols-5">
                  <div className="rounded-lg border border-zinc-800 bg-zinc-950/50 p-2">
                    <div className="text-[10px] text-zinc-600">executed</div>
                    <BoolPill value={action.executed} />
                  </div>
                  <div className="rounded-lg border border-zinc-800 bg-zinc-950/50 p-2">
                    <div className="text-[10px] text-zinc-600">wroteData</div>
                    <BoolPill value={action.wroteData} />
                  </div>
                  <div className="rounded-lg border border-zinc-800 bg-zinc-950/50 p-2">
                    <div className="text-[10px] text-zinc-600">realWritePath</div>
                    <BoolPill value={action.realWritePath} />
                  </div>
                  <div className="rounded-lg border border-zinc-800 bg-zinc-950/50 p-2">
                    <div className="text-[10px] text-zinc-600">memoryWrite</div>
                    <BoolPill value={action.memoryWriteEnabled} />
                  </div>
                  <div className="rounded-lg border border-zinc-800 bg-zinc-950/50 p-2">
                    <div className="text-[10px] text-zinc-600">legacyConfirm</div>
                    <BoolPill value={action.legacyConfirmEndpointUsed} />
                  </div>
                </div>

                <div className="mt-3 grid grid-cols-1 gap-2 md:grid-cols-3">
                  <Field label="intent" value={action.intent} />
                  <Field label="disposition" value={action.disposition} />
                  <Field label="riskLevel" value={action.riskLevel} />
                  <Field label="confirmTarget" value={action.confirmTarget} />
                  <Field label="confirmWriteExecutor" value={action.confirmWriteExecutorId} />
                  <Field label="confirmWriteDecision" value={action.confirmWriteDecisionReason} />
                  <Field label="confirmPlan" value={action.confirmPlanReason} />
                  <Field label="memoryTarget" value={action.memoryTarget} />
                  <Field label="safetyMode" value={action.safetyMode} />
                </div>
              </article>
            ))}
          </section>
        )}
      </div>
    </main>
  );
}

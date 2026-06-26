"use client";

import { useEffect, useState } from "react";
import { login, logout, getToken } from "./actions/auth";
import { auth } from "@/lib/firebase";
import { GoogleAuthProvider, signInWithPopup, signOut } from "firebase/auth";
import { IngestForm } from "@/components/IngestForm";
import { Timeline } from "@/components/Timeline";
import { ReminderWidget } from "@/components/ReminderWidget";
import { DailySummaryCard } from "@/components/DailySummaryCard";
import { Loader2 } from "lucide-react";

export default function Home() {
  const [isLoggedIn, setIsLoggedIn] = useState<boolean | null>(null);
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  useEffect(() => {
    // Check initial auth state from cookies
    getToken().then((token) => setIsLoggedIn(!!token));
  }, []);

  const handleLogin = async () => {
    try {
      // 1. Google SignIn via Firebase
      const provider = new GoogleAuthProvider();
      const result = await signInWithPopup(auth, provider);
      
      // 2. Get ID Token
      const idToken = await result.user.getIdToken();
      
      // 3. Send to Server Action to set HttpOnly Cookie
      await login(idToken);
      setIsLoggedIn(true);
    } catch (error) {
      console.error("Login failed:", error);
      // Fallback for local testing if Firebase config is missing
      if (process.env.NODE_ENV === "development") {
        await login("mock_local_token_123");
        setIsLoggedIn(true);
      }
    }
  };

  const handleLogout = async () => {
    await signOut(auth);
    await logout();
    setIsLoggedIn(false);
  };

  if (isLoggedIn === null) {
    return (
      <div className="min-h-screen bg-zinc-950 flex items-center justify-center">
        <Loader2 className="w-8 h-8 animate-spin text-zinc-500" />
      </div>
    );
  }

  return (
    <main className="min-h-screen bg-zinc-950 text-zinc-300 p-6 md:p-12 font-sans selection:bg-indigo-500/30">
      <div className="max-w-6xl mx-auto">
        <header className="flex justify-between items-center mb-12 border-b border-zinc-800/50 pb-6">
          <div>
            <h1 className="text-2xl font-bold bg-clip-text text-transparent bg-gradient-to-r from-indigo-400 to-cyan-400">
              Antigravity Life
            </h1>
            <p className="text-zinc-500 text-sm mt-1">Capture your moments.</p>
          </div>
          
          {isLoggedIn ? (
            <button onClick={handleLogout} className="text-sm text-zinc-400 hover:text-white transition-colors">
              Sign Out
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
            <h2 className="text-2xl font-semibold text-white mb-2">Welcome Back</h2>
            <p className="text-zinc-500 mb-8 max-w-sm">Sign in securely to record your life events and review your timeline.</p>
            <button
              onClick={handleLogin}
              className="bg-white hover:bg-zinc-100 text-zinc-900 px-6 py-3 rounded-xl font-medium transition-colors"
            >
              Sign in with Google
            </button>
            <p className="text-xs text-zinc-600 mt-4">
              (In dev mode without Firebase config, this acts as Mock Login)
            </p>
          </div>
        ) : (
          <div className="animate-in fade-in slide-in-from-bottom-4 duration-700 grid grid-cols-1 lg:grid-cols-3 gap-8">
            <div className="lg:col-span-2 space-y-6">
              <IngestForm onIngested={() => setRefreshTrigger(t => t + 1)} />
              <Timeline refreshTrigger={refreshTrigger} />
            </div>
            <div className="lg:col-span-1 space-y-6">
              <ReminderWidget refreshTrigger={refreshTrigger} onUpdated={() => setRefreshTrigger(t => t + 1)} />
              <DailySummaryCard refreshTrigger={refreshTrigger} />
            </div>
          </div>
        )}
      </div>
    </main>
  );
}

"use client";

import React, { createContext, useContext, useEffect, useState } from "react";
import {
  User,
  onIdTokenChanged,
  setPersistence,
  browserLocalPersistence,
  GoogleAuthProvider,
  signInWithPopup,
  signOut
} from "firebase/auth";
import { auth } from "@/lib/firebase";
import { login, logout, getToken } from "@/app/actions/auth";

interface MockUser {
  uid: string;
  email: string | null;
  displayName: string | null;
}

export interface AuthContextType {
  user: User | MockUser | null;
  loading: boolean;
  token: string | null;
  loginWithGoogle: () => Promise<void>;
  logoutUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<User | MockUser | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [token, setToken] = useState<string | null>(null);

  useEffect(() => {
    let unsubscribe: (() => void) | undefined;

    const initializeAuth = async () => {
      const isFirebaseConfigured = !!process.env.NEXT_PUBLIC_FIREBASE_API_KEY;

      if (isFirebaseConfigured) {
        try {
          // Explicitly configure Firebase Auth local persistence
          await setPersistence(auth, browserLocalPersistence);
        } catch (error) {
          console.error("Firebase persistence configuration failed:", error);
        }

        // Register ID Token change listener
        unsubscribe = onIdTokenChanged(auth, async (firebaseUser) => {
          try {
            if (firebaseUser) {
              const idToken = await firebaseUser.getIdToken();
              // Sync token to server-side HTTP-only cookie
              await login(idToken);
              setUser(firebaseUser);
              setToken(idToken);
            } else {
              // Clear server cookie when client logged out
              await logout();
              setUser(null);
              setToken(null);
            }
          } catch (error) {
            console.error("Error syncing auth token:", error);
            setUser(null);
            setToken(null);
          } finally {
            setLoading(false);
          }
        });
      } else {
        // Fallback for local development when Firebase is unconfigured
        if (process.env.NODE_ENV === "development") {
          try {
            const currentToken = await getToken();
            if (currentToken) {
              setUser({
                uid: "mock-uid-123",
                email: "mock@example.com",
                displayName: "Mock User",
              });
              setToken(currentToken);
            } else {
              setUser(null);
              setToken(null);
            }
          } catch (error) {
            console.error("Failed to read mock token:", error);
            setUser(null);
            setToken(null);
          } finally {
            setLoading(false);
          }
        } else {
          setLoading(false);
        }
      }
    };

    initializeAuth();

    return () => {
      if (unsubscribe) {
        unsubscribe();
      }
    };
  }, []);

  const loginWithGoogle = async () => {
    const isFirebaseConfigured = !!process.env.NEXT_PUBLIC_FIREBASE_API_KEY;

    if (isFirebaseConfigured) {
      const provider = new GoogleAuthProvider();
      await signInWithPopup(auth, provider);
    } else {
      if (process.env.NODE_ENV === "development") {
        const mockToken = "mock_local_token_123";
        await login(mockToken);
        setUser({
          uid: "mock-uid-123",
          email: "mock@example.com",
          displayName: "Mock User",
        });
        setToken(mockToken);
      } else {
        throw new Error("Firebase is not configured.");
      }
    }
  };

  const logoutUser = async () => {
    const isFirebaseConfigured = !!process.env.NEXT_PUBLIC_FIREBASE_API_KEY;

    if (isFirebaseConfigured) {
      await signOut(auth);
    } else {
      await logout();
      setUser(null);
      setToken(null);
    }
  };

  return (
    <AuthContext.Provider value={{ user, loading, token, loginWithGoogle, logoutUser }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
}

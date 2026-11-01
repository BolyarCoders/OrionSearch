"use client";

import React, { createContext, useCallback, useContext, useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { UserProfile } from "@/types/api";
import { tokenStore } from "../api/client";
import { useAppMode } from "./app-mode-context";

interface AuthContextType {
  user: UserProfile | null;
  /** False until the stored session (if any) has been checked. */
  ready: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | null>(null);
export const PUBLIC_PATHS = ["/login"];

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const { client, isLive } = useAppMode();
  const router = useRouter();
  const pathname = usePathname();
  const [user, setUser] = useState<UserProfile | null>(null);
  const [ready, setReady] = useState(false);

  const loadProfile = useCallback(async () => {
    if (!client.isAuthenticated()) { setUser(null); setReady(true); return; }
    try { setUser(await client.me()); } catch { setUser(null); }
    setReady(true);
  }, [client]);

  useEffect(() => { void loadProfile(); }, [loadProfile]);

  // Tokens cleared elsewhere (failed refresh, password change) end the session here too.
  useEffect(() => tokenStore.subscribe(() => { if (!tokenStore.read()) setUser(null); }), []);

  const needsSignIn = isLive && ready && !user && !PUBLIC_PATHS.includes(pathname);
  useEffect(() => { if (needsSignIn) router.replace("/login"); }, [needsSignIn, router]);

  const login = useCallback(async (email: string, password: string) => {
    await client.login(email, password);
    setUser(await client.me());
  }, [client]);

  const register = useCallback(async (email: string, password: string) => {
    await client.register(email, password);
    await login(email, password);
  }, [client, login]);

  const logout = useCallback(async () => {
    try { await client.logout(); } finally { setUser(null); router.replace("/login"); }
  }, [client, router]);

  const blocked = isLive && (!ready || needsSignIn);
  return (
    <AuthContext.Provider value={{ user, ready, login, register, logout }}>
      {blocked ? <div className="p-10 text-sm text-content-muted" role="status">Checking your session…</div> : children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within an AuthProvider");
  return ctx;
}

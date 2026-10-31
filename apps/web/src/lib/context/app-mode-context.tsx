"use client";

import React, { createContext, useContext, useEffect, useMemo, useState } from "react";
import { AppMode } from "@/types/api";
import { IApiClient, LiveApiClient } from "../api/client";
import { DemoApiClient } from "../api/demo-client";

interface AppModeContextType {
  mode: AppMode;
  setMode: (mode: AppMode) => void;
  client: IApiClient;
  isDemo: boolean;
  isLive: boolean;
}

const AppModeContext = createContext<AppModeContextType | null>(null);

// Singletons to preserve in-memory demo data across navigation
let sharedDemoClient: DemoApiClient | null = null;
let sharedLiveClient: LiveApiClient | null = null;

function getDemoClient(): DemoApiClient {
  if (!sharedDemoClient) {
    sharedDemoClient = new DemoApiClient();
  }
  return sharedDemoClient;
}

function getLiveClient(): LiveApiClient {
  if (!sharedLiveClient) {
    sharedLiveClient = new LiveApiClient();
  }
  return sharedLiveClient;
}

export function AppModeProvider({ children }: { children: React.ReactNode }) {
  const [mode, setModeState] = useState<AppMode>("demo");

  useEffect(() => {
    try {
      const saved = localStorage.getItem("orion_app_mode");
      if (saved === "demo" || saved === "live") {
        setModeState(saved);
      }
    } catch {
      // ignore localStorage error in SSR or restricted environments
    }
  }, []);

  const setMode = (newMode: AppMode) => {
    setModeState(newMode);
    try {
      localStorage.setItem("orion_app_mode", newMode);
    } catch {
      // ignore
    }
  };

  const client = useMemo(() => {
    return mode === "demo" ? getDemoClient() : getLiveClient();
  }, [mode]);

  return (
    <AppModeContext.Provider
      value={{
        mode,
        setMode,
        client,
        isDemo: mode === "demo",
        isLive: mode === "live",
      }}
    >
      <React.Fragment key={mode}>{children}</React.Fragment>
    </AppModeContext.Provider>
  );
}

export function useAppMode() {
  const ctx = useContext(AppModeContext);
  if (!ctx) {
    throw new Error("useAppMode must be used within an AppModeProvider");
  }
  return ctx;
}

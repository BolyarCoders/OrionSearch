"use client";

import React, { createContext, useContext } from "react";
import { IApiClient, LiveApiClient } from "../api/client";

interface ApiContextType {
  client: IApiClient;
}

const ApiContext = createContext<ApiContextType | null>(null);
const sharedClient = new LiveApiClient();

export function AppModeProvider({ children }: { children: React.ReactNode }) {
  return <ApiContext.Provider value={{ client: sharedClient }}>{children}</ApiContext.Provider>;
}

export function useAppMode() {
  const ctx = useContext(ApiContext);
  if (!ctx) {
    throw new Error("useAppMode must be used within an AppModeProvider");
  }
  return ctx;
}

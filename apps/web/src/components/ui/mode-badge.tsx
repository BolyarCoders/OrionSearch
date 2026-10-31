"use client";

import { DEFAULT_API_URL } from "@/lib/api/client";
import React, { useState } from "react";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Server, FolderArchive, ChevronDown, Check, ShieldAlert } from "lucide-react";

export function ModeBadge() {
  const { mode, setMode } = useAppMode();
  const [isOpen, setIsOpen] = useState(false);

  return (
    <div className="relative">
      <button
        onClick={() => setIsOpen(!isOpen)}
        className="flex items-center gap-2 px-3 py-1.5 rounded-control border border-border bg-surface hover:border-border-light text-xs font-medium transition-colors duration-micro"
        aria-label="Toggle application mode" aria-expanded={isOpen} onKeyDown={e => { if (e.key === "Escape") setIsOpen(false); }}
      >
        {mode === "demo" ? (
          <>
            <span className="w-2 h-2 rounded-full bg-status-success " />
            <span className="text-content-primary">Demo Mode</span>
            <span className="text-content-muted hidden sm:inline">Sample data</span>
          </>
        ) : (
          <>
            <span className="w-2 h-2 rounded-full bg-accent" />
            <span className="text-content-primary">Live API</span>
            <span className="text-content-muted hidden sm:inline"></span>
          </>
        )}
        <ChevronDown className="w-3.5 h-3.5 text-content-muted ml-0.5" />
      </button>

      {isOpen && (
        <>
          <div
            className="fixed inset-0 z-30"
            onClick={() => setIsOpen(false)}
            aria-hidden="true"
          />
          <div className="absolute right-0 mt-2 w-[min(20rem,calc(100vw-2rem))] bg-surface border border-border rounded-panel shadow-xl p-2 z-40 text-xs">
            <div className="px-2 py-1.5 font-medium text-content-primary border-b border-border/80 mb-1 flex items-center justify-between">
              <span>Environment Mode</span>
              <span className="text-xs text-content-muted uppercase tracking-wider">Select Mode</span>
            </div>

            <button
              onClick={() => {
                setMode("demo");
                setIsOpen(false);
              }}
              className={`w-full flex items-start gap-2.5 p-2 rounded-control text-left transition-colors ${
                mode === "demo" ? "bg-accent/15 border border-accent/30" : "hover:bg-surface-raised"
              }`}
            >
              <FolderArchive className="w-4 h-4 text-status-success mt-0.5 shrink-0" />
              <div className="flex-1">
                <div className="font-semibold text-content-primary flex items-center justify-between">
                  <span>Demo Mode</span>
                  {mode === "demo" && <Check className="w-3.5 h-3.5 text-accent" />}
                </div>
                <p className="text-xs text-content-muted mt-0.5">
                  Realistic in-memory mock data with 6 reference archives, simulated sources, and research runs.
                </p>
              </div>
            </button>

            <button
              onClick={() => {
                setMode("live");
                setIsOpen(false);
              }}
              className={`w-full flex items-start gap-2.5 p-2 rounded-control text-left transition-colors mt-1 ${
                mode === "live" ? "bg-accent/15 border border-accent/30" : "hover:bg-surface-raised"
              }`}
            >
              <Server className="w-4 h-4 text-accent mt-0.5 shrink-0" />
              <div className="flex-1">
                <div className="font-semibold text-content-primary flex items-center justify-between">
                  <span>Live API Mode</span>
                  {mode === "live" && <Check className="w-3.5 h-3.5 text-accent" />}
                </div>
                <p className="text-xs text-content-muted mt-0.5">
                  Connects to {DEFAULT_API_URL}. Protected routes require authentication.
                </p>
              </div>
            </button>

            <div className="mt-2 pt-2 border-t border-border/80 px-2 pb-1 text-xs text-content-muted flex items-start gap-1.5">
              <ShieldAlert className="w-3.5 h-3.5 text-status-warning shrink-0 mt-0.5" />
              <span>Backend authentication is currently unconfigured; protected live routes will reject requests with 401.</span>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

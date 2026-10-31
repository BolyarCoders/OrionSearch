"use client";

import { DEFAULT_API_URL } from "@/lib/api/client";
import { ApiProblem } from "./api-problem";
import React from "react";
import { ShieldAlert, ArrowRight, Search } from "lucide-react";
import { Button } from "./button";
import { useAppMode } from "@/lib/context/app-mode-context";

interface AuthNoticeProps {
  error?: unknown;
  onRetry?: () => void;
}

export function AuthNotice({ error, onRetry }: AuthNoticeProps) {
  const { setMode } = useAppMode();

  return (
    <div className="rounded-panel border border-status-warning/30 bg-status-warning/5 p-6 max-w-2xl mx-auto my-8">
      <div className="flex items-start gap-4">
        <div className="p-2.5 rounded-full bg-status-warning/15 text-status-warning shrink-0">
          <ShieldAlert className="w-6 h-6" />
        </div>
        <div className="flex-1 min-w-0">
          <h3 className="text-base font-semibold text-content-primary">
            Live access is unavailable
          </h3>
          <p className="text-sm text-content-secondary mt-1.5 leading-relaxed">
            The service requires authentication, and sign-in is not available yet.
            You can retry the request or explore the demo workspace.
          </p>
          <ApiProblem error={error} />

          <div className="mt-5 flex flex-wrap items-center gap-3">
            <Button
              variant="primary"
              size="sm"
              leftIcon={<Search className="w-4 h-4" />}
              onClick={() => setMode("demo")}
            >
              Switch to Demo Mode
            </Button>
            {onRetry && (
              <Button variant="secondary" size="sm" onClick={onRetry}>
                Retry Live Request
              </Button>
            )}
            <a
              href={`${DEFAULT_API_URL}/health`}
              target="_blank"
              rel="noreferrer"
              className="text-xs text-content-muted hover:text-content-primary underline flex items-center gap-1 ml-auto"
            >
              Service status <ArrowRight className="w-3 h-3" />
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}

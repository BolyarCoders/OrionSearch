"use client";

import { DEFAULT_API_URL } from "@/lib/api/client";
import { ApiProblem } from "./api-problem";
import React from "react";
import Link from "next/link";
import { ShieldAlert, ArrowRight } from "lucide-react";
import { Button } from "./button";

interface AuthNoticeProps {
  error?: unknown;
  onRetry?: () => void;
}

export function AuthNotice({ error, onRetry }: AuthNoticeProps) {
  return (
    <div className="rounded-panel border border-status-warning/30 bg-status-warning/5 p-6 max-w-2xl mx-auto my-8">
      <div className="flex items-start gap-4">
        <div className="p-2.5 rounded-full bg-status-warning/15 text-status-warning shrink-0">
          <ShieldAlert className="w-6 h-6" />
        </div>
        <div className="flex-1 min-w-0">
          <h3 className="text-base font-semibold text-content-primary">
            Моля, влезте в профила си
          </h3>
          <p className="text-sm text-content-secondary mt-1.5 leading-relaxed">
            Сесията ви липсва или е изтекла. Влезте отново, за да продължите.
          </p>
          <ApiProblem error={error} />

          <div className="mt-5 flex flex-wrap items-center gap-3">
            <Link href="/login"><Button variant="primary" size="sm">Вход</Button></Link>
            {onRetry && (
              <Button variant="ghost" size="sm" onClick={onRetry}>
                Опитай отново
              </Button>
            )}
            <a
              href={`${DEFAULT_API_URL}/health`}
              target="_blank"
              rel="noreferrer"
              className="text-xs text-content-muted hover:text-content-primary underline flex items-center gap-1 ml-auto"
            >
              Състояние на услугата <ArrowRight className="w-3 h-3" />
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}

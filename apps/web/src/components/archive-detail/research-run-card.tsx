"use client";

import React from "react";
import { ResearchRunStatus } from "@/types/api";
import { useResearchRun } from "@/lib/hooks/use-research-run";
import { ApiProblem } from "../ui/api-problem";
import { ReportPanel } from "./report-panel";
import { StatusPill } from "../ui/status-pill";
import { useAppMode } from "@/lib/context/app-mode-context";
import { formatRelativeTime } from "@/lib/normalization";
import {
  RotateCw,
  AlertCircle,
  Clock,
  Search,
  FileText,
  FileCheck2,
  Lightbulb,
  CheckCircle2,
} from "lucide-react";

interface ResearchRunCardProps {
  initialJob: ResearchRunStatus;
  onJobCompleted?: () => void;
  onJobUpdated?: (job: ResearchRunStatus) => void;
}

export function ResearchRunCard({ initialJob, onJobCompleted, onJobUpdated }: ResearchRunCardProps) {
  const { job, error, retry } = useResearchRun(initialJob, onJobCompleted, onJobUpdated);
  const isTerminal = job.status === "completed" || job.status === "failed" || job.status === "cancelled";

  return (
    <div className="bg-surface border border-border rounded-panel p-6 space-y-6">
      <ApiProblem error={error} onRetry={retry} />
      {/* Run Header */}
      <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 pb-5 border-b border-border/80">
        <div>
          <div className="flex items-center gap-2.5">
            <h3 className="text-base font-semibold text-content-primary">Следено проучване</h3>
            <StatusPill status={job.status} />
          </div>
          <div className="text-xs text-content-muted mt-1 font-mono break-all">ID на проучването: {job.id}</div>
        </div>

        <div className="flex items-center gap-2 text-xs text-content-muted">
          <Clock className="w-3.5 h-3.5" />
          <span>Създадено {formatRelativeTime(job.createdAt)}</span>
        </div>
      </div>

      {/* Indeterminate Running / Retrying Status Banner */}
      {!isTerminal && (
        <div className="p-4 rounded-control bg-accent/10 border border-accent/25 flex items-center gap-3">
          <RotateCw className="w-5 h-5 text-accent animate-spin shrink-0" />
          <div className="text-xs">
            <div className="font-semibold text-content-primary">
              {job.status === "queued"
                ? job.retrying
                  ? "На опашка за повторен опит със закъснение…"
                  : "На опашка за фонова обработка…"
                : "Проучването се изпълнява…"}
            </div>
            <p className="text-content-secondary mt-0.5">
              Статусът се обновява, докато този изглед е отворен. Задачите на опашка може да изчакат свободен работник.
            </p>
          </div>
        </div>
      )}

      {job.retrying && <p className="text-xs text-content-muted">{job.error} {job.nextAttemptAt && `Следващ опит след ${new Date(job.nextAttemptAt).toLocaleString("bg-BG")}.`} Това е най-ранното време, не гарантиран старт.</p>}
      {job.status === "cancelled" && <p role="status" className="text-sm text-status-warning">Проучването е отменено. Статусът му вече няма да се проверява.</p>}
      {/* Failure Banner */}
      {job.status === "failed" && (
        <div className="p-4 rounded-control bg-status-failure/10 border border-status-failure/30 flex items-start gap-3">
          <AlertCircle className="w-5 h-5 text-status-failure shrink-0 mt-0.5" />
          <div className="text-xs">
            <div className="font-semibold text-status-failure">Проучването е неуспешно</div>
            <p className="text-content-secondary mt-0.5">
              {job.error || "Фоновият работник срещна непоправима грешка."}
            </p>
          </div>
        </div>
      )}

      {/* Completion Banner */}
      {job.status === "completed" && (
        <div className="p-4 rounded-control bg-status-success/10 border border-status-success/30 flex items-center gap-3">
          <CheckCircle2 className="w-5 h-5 text-status-success shrink-0" />
          <div className="text-xs">
            <div className="font-semibold text-status-success">Проучването е завършено</div>
            <p className="text-content-secondary mt-0.5">
              Намерените източници и находки са записани в архива.
            </p>
          </div>
        </div>
      )}

      {/* Statistics Matrix */}
      <div>
        <h4 className="text-xs font-semibold uppercase tracking-wider text-content-muted mb-3">
          Статистика на резултата
        </h4>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          <div className="bg-surface-raised border border-border rounded-control p-3 flex items-center gap-3">
            <div className="p-2 rounded-control bg-surface text-accent">
              <Search className="w-4 h-4" />
            </div>
            <div>
              <div className="text-lg font-bold text-content-primary">{job.statistics.queries}</div>
              <div className="text-xs text-content-muted">Заявки</div>
            </div>
          </div>

          <div className="bg-surface-raised border border-border rounded-control p-3 flex items-center gap-3">
            <div className="p-2 rounded-control bg-surface text-status-info">
              <FileText className="w-4 h-4" />
            </div>
            <div>
              <div className="text-lg font-bold text-content-primary">{job.statistics.sources}</div>
              <div className="text-xs text-content-muted">Намерени източници</div>
            </div>
          </div>

          <div className="bg-surface-raised border border-border rounded-control p-3 flex items-center gap-3">
            <div className="p-2 rounded-control bg-surface text-status-success">
              <FileCheck2 className="w-4 h-4" />
            </div>
            <div>
              <div className="text-lg font-bold text-content-primary">{job.statistics.versions}</div>
              <div className="text-xs text-content-muted">Запазени копия</div>
            </div>
          </div>

          <div className="bg-surface-raised border border-border rounded-control p-3 flex items-center gap-3">
            <div className="p-2 rounded-control bg-surface text-status-warning">
              <Lightbulb className="w-4 h-4" />
            </div>
            <div>
              <div className="text-lg font-bold text-content-primary">{job.statistics.findings}</div>
              <div className="text-xs text-content-muted">Находки</div>
            </div>
          </div>
        </div>
      </div>

      {job.status === "completed" && <ReportPanel runId={job.id} />}

      {/* Metadata & Attempt Tracking */}
      <div className="pt-4 border-t border-border/80 grid grid-cols-1 sm:grid-cols-3 gap-3 text-xs text-content-muted">
        <div>
          <span className="text-content-secondary font-medium">Опити: </span>
          <span>
            {job.attempts} / {job.maxAttempts}
          </span>
        </div>
        {job.startedAt && (
          <div>
            <span className="text-content-secondary font-medium">Започнато: </span>
            <span>{formatRelativeTime(job.startedAt)}</span>
          </div>
        )}
        {job.finishedAt && (
          <div>
            <span className="text-content-secondary font-medium">Приключено: </span>
            <span>{formatRelativeTime(job.finishedAt)}</span>
          </div>
        )}
      </div>
    </div>
  );
}

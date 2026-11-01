"use client";
import React, { useCallback, useEffect, useState } from "react";
import { ResearchRunStatus } from "@/types/api";
import { useAppMode } from "@/lib/context/app-mode-context";
import { formatRelativeTime } from "@/lib/normalization";
import { ApiProblem } from "../ui/api-problem";
import { StatusPill } from "../ui/status-pill";
import { Button } from "../ui/button";

export function RunHistory({ archiveId, selectedId, refreshKey, onSelect }: { archiveId: string; selectedId?: string; refreshKey?: unknown; onSelect: (run: ResearchRunStatus) => void }) {
  const { client } = useAppMode();
  const [page, setPage] = useState(1);
  const [runs, setRuns] = useState<ResearchRunStatus[]>([]);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState<unknown>(null);
  const pageSize = 10;
  const load = useCallback(async () => {
    try {
      const res = await client.listResearchJobs(archiveId, { page, pageSize });
      setRuns(res.items); setTotal(res.totalCount); setError(null);
    } catch (e) { setError(e); }
  }, [client, archiveId, page]);
  useEffect(() => { void load(); }, [load, refreshKey]);
  return (
    <div className="bg-surface border border-border rounded-panel p-5 space-y-3">
      <h3 className="text-sm font-semibold text-content-primary">Run history</h3>
      <ApiProblem error={error} onRetry={load} />
      {!error && runs.length === 0 && <p className="text-xs text-content-muted">No research runs yet.</p>}
      <ul className="divide-y divide-border/40">
        {runs.map(run => (
          <li key={run.id}>
            <button type="button" onClick={() => onSelect(run)} aria-current={run.id === selectedId ? "true" : undefined} className={`w-full flex items-center justify-between gap-3 py-2.5 px-2 text-left text-xs rounded-control hover:bg-surface-raised ${run.id === selectedId ? "bg-surface-raised" : ""}`}>
              <span className="font-mono truncate text-content-secondary">{run.id.slice(0, 8)}… · {formatRelativeTime(run.createdAt)}</span>
              <StatusPill status={run.status} />
            </button>
          </li>
        ))}
      </ul>
      {total > pageSize && <div className="flex items-center justify-between text-xs text-content-muted">
        <Button variant="secondary" size="sm" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</Button>
        <span>Page {page} of {Math.ceil(total / pageSize)}</span>
        <Button variant="secondary" size="sm" disabled={page * pageSize >= total} onClick={() => setPage(page + 1)}>Next</Button>
      </div>}
    </div>
  );
}

"use client";
import React, { useCallback, useEffect, useState } from "react";
import { FindingDetails, FindingStatus, FindingSummary, FindingType } from "@/types/api";
import { useAppMode } from "@/lib/context/app-mode-context";
import { formatRelativeTime } from "@/lib/normalization";
import { ApiProblem } from "../ui/api-problem";
import { Button } from "../ui/button";

export function FindingsPanel({ archiveId }: { archiveId: string }) {
  const { client } = useAppMode();
  const [items, setItems] = useState<FindingSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<FindingStatus | "">("");
  const [type, setType] = useState<FindingType | "">("");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<unknown>(null);
  const [open, setOpen] = useState<FindingDetails | null>(null);
  const pageSize = 20;

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await client.listFindings(archiveId, { page, pageSize, status: status || undefined, type: type || undefined });
      setItems(res.items); setTotal(res.totalCount); setError(null);
    } catch (e) { setItems([]); setTotal(0); setError(e); }
    finally { setLoading(false); }
  }, [client, archiveId, page, status, type]);
  useEffect(() => { void load(); }, [load]);

  async function show(id: string) {
    try { setOpen(await client.getFinding(archiveId, id)); setError(null); } catch (e) { setError(e); }
  }
  const select = "bg-surface-raised border border-border rounded-control text-xs p-2";
  return (
    <div className="bg-surface border border-border rounded-panel p-5 space-y-4">
      <div className="flex flex-wrap gap-3 items-center justify-between">
        <h3 className="text-sm font-semibold text-content-primary">Findings</h3>
        <div className="flex gap-2">
          <select aria-label="Finding status" className={select} value={status} onChange={e => { setStatus(e.target.value as FindingStatus | ""); setPage(1); }}>
            <option value="">Any status</option>{["unverified", "supported", "disputed", "superseded"].map(v => <option key={v}>{v}</option>)}
          </select>
          <select aria-label="Finding type" className={select} value={type} onChange={e => { setType(e.target.value as FindingType | ""); setPage(1); }}>
            <option value="">Any type</option>{["claim", "observation", "estimate"].map(v => <option key={v}>{v}</option>)}
          </select>
        </div>
      </div>
      <ApiProblem error={error} onRetry={load} />
      {loading ? <p className="text-xs text-content-muted" role="status">Loading findings…</p>
        : items.length === 0 && !error ? <p className="text-xs text-content-muted">No findings yet. A completed research run does not always produce findings.</p> : null}
      <ul className="divide-y divide-border/40">
        {items.map(f => (
          <li key={f.id} className="py-3 space-y-1">
            <button type="button" className="text-left text-sm text-content-primary hover:text-accent" onClick={() => void show(f.id)}>{f.statement}{f.statementTruncated ? "…" : ""}</button>
            <div className="text-xs text-content-muted">{f.type} · {f.status} · {f.evidenceCount} evidence · {formatRelativeTime(f.firstObservedAt)}{f.confidence !== null ? ` · confidence ${f.confidence}` : ""}</div>
          </li>
        ))}
      </ul>
      {open && (
        <div className="border border-border rounded-control p-4 space-y-2 text-xs">
          <div className="flex justify-between"><strong className="text-content-primary">Evidence for selected finding</strong><button className="text-accent" onClick={() => setOpen(null)}>Close</button></div>
          {open.evidence.items.length === 0 && <p className="text-content-muted">No evidence recorded.</p>}
          {open.evidence.items.map(ev => (
            <div key={ev.id} className="border-t border-border/50 pt-2">
              <a href={ev.canonicalUrl} target="_blank" rel="noreferrer" className="text-accent hover:underline">{ev.sourceTitle || ev.canonicalUrl}</a>
              <span className="text-content-muted"> · {ev.stance}</span>
              {ev.quote && <blockquote className="mt-1 text-content-secondary whitespace-pre-wrap">{ev.quote}{ev.quoteTruncated ? "…" : ""}</blockquote>}
            </div>
          ))}
        </div>
      )}
      {total > pageSize && <div className="flex items-center justify-between text-xs text-content-muted">
        <Button variant="secondary" size="sm" disabled={page === 1} onClick={() => setPage(page - 1)}>Previous</Button>
        <span>Page {page} of {Math.ceil(total / pageSize)}</span>
        <Button variant="secondary" size="sm" disabled={page * pageSize >= total} onClick={() => setPage(page + 1)}>Next</Button>
      </div>}
    </div>
  );
}

"use client";
import React, { useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { useAppMode } from "@/lib/context/app-mode-context";
import { ApiProblem } from "../ui/api-problem";

/** AI report for a completed run. Rendered strictly as text, never HTML. */
export function ReportPanel({ runId }: { runId: string }) {
  const { client } = useAppMode();
  const [text, setText] = useState<string | null>(null);
  const [missing, setMissing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  useEffect(() => {
    let live = true;
    setText(null); setMissing(false); setError(null);
    client.getResearchReport(runId)
      .then(r => { if (live) setText(r.text); })
      .catch(e => { if (!live) return; if (e instanceof ApiError && e.status === 404) setMissing(true); else setError(e); });
    return () => { live = false; };
  }, [client, runId]);
  return (
    <div className="pt-4 border-t border-border/80 space-y-2">
      <h4 className="text-xs font-semibold uppercase tracking-wider text-content-muted">AI report</h4>
      <ApiProblem error={error} />
      {text !== null && <p className="text-sm text-content-secondary whitespace-pre-wrap leading-relaxed">{text}</p>}
      {text !== null && <p className="text-xs text-content-muted">Generated from source metadata and feed summaries. It is not fact-checked and may contain errors.</p>}
      {missing && <p className="text-xs text-content-muted">No report was produced for this run.</p>}
      {text === null && !missing && !error && <p className="text-xs text-content-muted" role="status">Loading report…</p>}
    </div>
  );
}

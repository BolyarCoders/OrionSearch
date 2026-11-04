"use client";
import React, { useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { useAppMode } from "@/lib/context/app-mode-context";
import { ApiProblem } from "../ui/api-problem";
import { ReportContent } from "./report-content";

/** AI доклад for a completed run. Rendered strictly as text, never HTML. */
export function ReportPanel({ runId }: { runId: string }) {
  const { client } = useAppMode();
  const [text, setText] = useState<string | null>(null);
  const [missing, setMissing] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let live = true;
    setText(null); setMissing(false); setError(null);
    client.getResearchReport(runId)
      .then(r => { if (live) setText(r.text); })
      .catch(e => { if (!live) return; if (e instanceof ApiError && e.status === 404) setMissing(true); else setError(e); });
    return () => { live = false; };
  }, [client, runId, attempt]);
  return (
    <div className="pt-4 border-t border-border/80 space-y-2">
      <h4 className="text-xs font-semibold uppercase tracking-wider text-content-muted">AI обзор на тенденциите</h4>
      <ApiProblem error={error} onRetry={() => setAttempt(value => value + 1)} />
      {text !== null && <ReportContent text={text} runId={runId} />}
      {text !== null && <p className="text-xs text-content-muted">AI обзор на събраните публикации. Използва откъси от достъпни статии и кратки описания, когато пълният текст не е наличен. Твърденията може да съдържат неточности; проверявайте важната информация в цитираните източници.</p>}
      {missing && <p className="text-xs text-content-muted">Докладът не е наличен или нямате достъп до него.</p>}
      {text === null && !missing && !error && <p className="text-xs text-content-muted" role="status">Зареждане на доклада…</p>}
    </div>
  );
}

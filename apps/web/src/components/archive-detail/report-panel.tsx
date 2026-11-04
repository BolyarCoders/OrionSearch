"use client";
import React, { useEffect, useState } from "react";
import { ApiError } from "@/lib/api/client";
import { useAppMode } from "@/lib/context/app-mode-context";
import { ApiProblem } from "../ui/api-problem";
import { renderMarkdown, splitReport } from "@/lib/markdown";

/** AI доклад for a completed run. Rendered strictly as text, never HTML. */
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
      <h4 className="text-xs font-semibold uppercase tracking-wider text-content-muted">AI доклад</h4>
      <ApiProblem error={error} />
      {text !== null && <ReportBody text={text} />}
      {text !== null && <p className="text-xs text-content-muted">AI синтез на наличните източници (откъси от страниците, където са достъпни, иначе кратки описания). Не е независимо проверен за фактите и може да съдържа грешки.</p>}
      {missing && <p className="text-xs text-content-muted">За това проучване няма доклад.</p>}
      {text === null && !missing && !error && <p className="text-xs text-content-muted" role="status">Зареждане на доклада…</p>}
    </div>
  );
}

function ReportBody({ text }: { text: string }) {
  const { body, sources } = splitReport(text);
  return (
    <>
      <div className="text-sm text-content-secondary leading-relaxed space-y-3 break-words">{renderMarkdown(body)}</div>
      {sources.length > 0 && (
        <details className="text-xs text-content-muted">
          <summary className="cursor-pointer font-semibold">Източници ({sources.length})</summary>
          <ol className="mt-2 space-y-1 list-none">
            {sources.map(s => (
              <li key={s.index} id={`source-${s.index}`} className="break-words">
                [{s.index}] <a href={s.url} target="_blank" rel="noopener noreferrer nofollow" className="underline underline-offset-2 text-accent hover:text-accent-hover">{s.title}</a>
              </li>
            ))}
          </ol>
        </details>
      )}
    </>
  );
}

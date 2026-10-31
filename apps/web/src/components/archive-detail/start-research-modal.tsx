"use client";
import React, { useEffect, useRef, useState } from "react";
import { Modal } from "../ui/modal";
import { Button } from "../ui/button";
import { Textarea } from "../ui/textarea";
import { ApiProblem } from "../ui/api-problem";
import { useAppMode } from "@/lib/context/app-mode-context";
import { ResearchRunStatus, SubmitResearchJobRequest } from "@/types/api";
import { ApiError } from "@/lib/api/client";
import { Search } from "lucide-react";
interface Props { isOpen: boolean; onClose: () => void; archiveId: string; archiveTitle: string; canSubmit?: boolean; onJobStarted: (job: ResearchRunStatus) => void; }
interface Pending { key: string; request: SubmitResearchJobRequest; }
export function StartResearchModal({ isOpen, onClose, archiveId, archiveTitle, canSubmit = true, onJobStarted }: Props) {
  const { client, mode } = useAppMode();
  const storageKey = `orion-pending:${mode}:${archiveId}`;
  const [objective, setObjective] = useState("");
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [locked, setLocked] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const pending = useRef<Pending | null>(null);
  const busy = useRef(false);
  useEffect(() => {
    if (!isOpen) return;
    setError(null); pending.current = null; setLocked(false); setObjective("");
    try {
      const saved = JSON.parse(sessionStorage.getItem(storageKey) || "null") as Pending | null;
      if (saved && typeof saved.key === "string" && saved.request && (saved.request.objective == null || typeof saved.request.objective === "string")) {
        pending.current = saved; setObjective(saved.request.objective || ""); setLocked(true);
      }
    } catch { /* Storage is optional; in-memory retries keep their key. */ }
  }, [isOpen, storageKey]);
  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (busy.current || !canSubmit) return;
    if (objective.length > 2000) { setError(new ApiError(400, "Objective cannot exceed 2000 characters.")); return; }
    busy.current = true; setIsSubmitting(true); setError(null);
    if (!pending.current) {
      pending.current = { key: `web-${crypto.randomUUID()}`, request: objective.trim() ? { objective: objective.trim() } : {} };
      try { sessionStorage.setItem(storageKey, JSON.stringify(pending.current)); } catch { /* optional */ }
    }
    setLocked(true);
    try {
      const job = await client.submitResearchJob(archiveId, pending.current.request, pending.current.key);
      try { sessionStorage.removeItem(storageKey); } catch { /* optional */ }
      pending.current = null; setLocked(false);
      onJobStarted(job); onClose();
    } catch (failure) { setError(failure); }
    finally { busy.current = false; setIsSubmitting(false); }
  }
  return <Modal isOpen={isOpen} onClose={() => { if (!busy.current) onClose(); }} title="Start research run" description={`Research for “${archiveTitle}”`}>
    <form onSubmit={submit} className="space-y-4">
      <ApiProblem error={error} />
      <Textarea label="Research objective (optional)" placeholder={`Default: ${archiveTitle}`} value={objective} maxChars={2000} disabled={locked || !canSubmit} onChange={e => setObjective(e.target.value)} hint="Leave empty to use the archive title. Archive preferences are captured when the run is accepted." />
      <p className="text-xs text-content-muted leading-relaxed">An accepted request is queued; it may wait until a research worker is available. You can follow its status in the Research tab.</p>
      {locked && <div className="text-xs text-content-muted space-y-2"><p>A previous submission may already have been accepted. Try again to retrieve that same run with the same request key.</p><Button type="button" variant="ghost" disabled={isSubmitting} onClick={() => { pending.current = null; setLocked(false); setError(null); try { sessionStorage.removeItem(storageKey); } catch { /* optional */ } }}>Create a separate request instead</Button></div>}
      <div className="flex justify-end gap-2 border-t border-border pt-4"><Button type="button" variant="ghost" disabled={isSubmitting} onClick={onClose}>Close</Button><Button type="submit" disabled={!canSubmit} isLoading={isSubmitting} leftIcon={<Search className="w-4 h-4" />}>{locked ? "Try same request again" : "Queue research"}</Button></div>
    </form>
  </Modal>;
}

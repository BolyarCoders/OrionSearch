"use client";
import React, { useEffect, useState } from "react";
import { CapturedVersion, CapturedVersionSummary, SourceSummary } from "@/types/api";
import { useAppMode } from "@/lib/context/app-mode-context";
import { Modal } from "../ui/modal";
import { ApiProblem } from "../ui/api-problem";
import { Button } from "../ui/button";

export function SourceVersionsModal({ archiveId, source, onClose }: { archiveId: string; source: SourceSummary | null; onClose: () => void }) {
  const { client } = useAppMode();
  const [versions, setVersions] = useState<CapturedVersionSummary[]>([]);
  const [detail, setDetail] = useState<CapturedVersion | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const docId = source?.documentId;
  useEffect(() => {
    setVersions([]); setDetail(null); setError(null);
    if (!docId) return;
    let live = true;
    setLoading(true);
    client.listVersions(archiveId, docId, { pageSize: 50 })
      .then(r => { if (live) setVersions(r.items); })
      .catch(e => { if (live) setError(e); })
      .finally(() => { if (live) setLoading(false); });
    return () => { live = false; };
  }, [client, archiveId, docId]);

  async function open(id: string) {
    try { setDetail(await client.getVersion(archiveId, docId!, id)); setError(null); } catch (e) { setError(e); }
  }
  async function download(id: string) {
    try {
      const blob = await client.downloadRaw(archiveId, docId!, id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url; a.download = `capture-${id}.bin`; a.click();
      URL.revokeObjectURL(url);
    } catch (e) { setError(e); }
  }
  return (
    <Modal isOpen={!!source} onClose={onClose} title="Запазени версии" description={source?.title || source?.canonicalUrl}>
      <div className="space-y-3 text-xs">
        <ApiProblem error={error} />
        {loading && <p role="status" className="text-content-muted">Зареждане на версиите…</p>}
        {!loading && versions.length === 0 && !error && <p className="text-content-muted">Няма запазена версия за този източник. Намерената връзка не винаги се запазва.</p>}
        <ul className="divide-y divide-border/40">
          {versions.map(v => (
            <li key={v.id} className="py-2 flex items-center justify-between gap-2">
              <span className="text-content-secondary">{new Date(v.fetchedAt).toLocaleString()}</span>
              <span className="flex gap-2">
                {v.hasText && <Button size="sm" variant="secondary" onClick={() => void open(v.id)}>Текст</Button>}
                {v.hasRaw && <Button size="sm" variant="secondary" onClick={() => void download(v.id)}>Изтегли оригинала</Button>}
              </span>
            </li>
          ))}
        </ul>
        {detail && <div className="border border-border rounded-control p-3 space-y-2">
          <p className="font-mono text-content-muted break-all">SHA-256 {detail.contentHash}</p>
          <pre className="whitespace-pre-wrap text-content-secondary max-h-72 overflow-auto">{detail.extractedText ?? "Няма извлечен текст."}</pre>
        </div>}
      </div>
    </Modal>
  );
}

import React from "react";
import { StatusPill } from "../ui/status-pill";
import { FileText } from "lucide-react";

interface ArchiveCardPreviewProps { title: string; description: string; templateKey?: string; }
export function ArchiveCardPreview({ title, description }: ArchiveCardPreviewProps) {
  return <div className="w-full max-w-sm mx-auto">
    <p className="text-xs text-content-muted mb-6">Преглед на архива</p>
    <div className="dossier p-5 min-h-[252px] flex flex-col">
      <p className="text-xs text-content-muted mb-4">Архив за проучване</p>
      <h4 className="text-lg font-semibold dossier-title">{title.trim() || "Заглавие на архива"}</h4>
      <p className="text-sm text-content-secondary mt-2 leading-relaxed">{description.trim() || "Добавете описание, за да определите какво искате да проучите."}</p>
      <div className="mt-auto pt-5"><div className="border-t border-border pt-4 flex justify-between items-center"><span className="text-xs text-content-secondary flex items-center gap-2"><FileText className="w-4 h-4" />0 източника</span><StatusPill status="active" /></div><p className="text-xs text-content-muted mt-3">Източниците ще се появят след проучване.</p></div>
    </div>
  </div>;
}

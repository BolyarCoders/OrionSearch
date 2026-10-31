import React from "react";
import { StatusPill } from "../ui/status-pill";
import { FileText } from "lucide-react";

interface ArchiveCardPreviewProps { title: string; description: string; templateKey?: string; }
export function ArchiveCardPreview({ title, description }: ArchiveCardPreviewProps) {
  return <div className="w-full max-w-sm mx-auto">
    <p className="text-xs text-content-muted mb-6">Archive preview</p>
    <div className="dossier p-5 min-h-[252px] flex flex-col">
      <p className="text-xs text-content-muted mb-4">Research archive</p>
      <h4 className="text-lg font-semibold dossier-title">{title.trim() || "Your archive title"}</h4>
      <p className="text-sm text-content-secondary mt-2 leading-relaxed">{description.trim() || "Add a description to define what you want to research."}</p>
      <div className="mt-auto pt-5"><div className="border-t border-border pt-4 flex justify-between items-center"><span className="text-xs text-content-secondary flex items-center gap-2"><FileText className="w-4 h-4" />0 sources</span><StatusPill status="active" /></div><p className="text-xs text-content-muted mt-3">Sources will appear after research runs.</p></div>
    </div>
  </div>;
}

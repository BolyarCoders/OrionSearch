"use client";

import React, { useEffect } from "react";
import { useRouter } from "next/navigation";
import { Search, Menu } from "lucide-react";
import { ModeBadge } from "../ui/mode-badge";

export function Topbar({ onOpenSidebar }: { onOpenSidebar: () => void }) {
  const router = useRouter();
  const findArchive = () => {
    const input = document.querySelector<HTMLInputElement>('input[aria-label="Search archives"]');
    if (input) { input.focus(); input.select(); }
    else router.push("/archives?focus=search");
  };
  useEffect(() => {
    const keydown = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault(); findArchive();
      }
    };
    window.addEventListener("keydown", keydown);
    return () => window.removeEventListener("keydown", keydown);
  });
  return (
    <header className="sticky top-0 z-30 h-16 bg-workspace border-b border-border px-4 sm:px-6 lg:px-10 flex items-center justify-between gap-3">
      <div className="flex items-center gap-3 min-w-0">
        <button onClick={onOpenSidebar} className="lg:hidden p-2 text-content-secondary rounded-control hover:bg-surface-raised" aria-label="Open sidebar"><Menu className="w-5 h-5" /></button>
        <span className="text-sm text-content-secondary hidden sm:block">Research library</span>
        <span className="text-sm text-content-secondary sm:hidden">Library</span>
      </div>
      <div className="flex items-center gap-3 sm:gap-6">
        <button onClick={findArchive} className="hidden sm:flex items-center gap-2 text-xs text-content-secondary hover:text-content-primary px-2 rounded-control" aria-label="Find archive"><Search className="w-4 h-4" /><span>Find archive</span><kbd className="ml-2 text-content-muted border border-border rounded px-1.5">⌘ / Ctrl K</kbd></button>
        <ModeBadge />
      </div>
    </header>
  );
}

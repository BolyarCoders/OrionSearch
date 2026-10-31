"use client";

import React, { useEffect, useRef } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { FolderArchive, Settings, ArrowUpRight, X, Plus } from "lucide-react";

export function Sidebar({ isOpen, onClose }: { isOpen: boolean; onClose: () => void }) {
  const pathname = usePathname();
  const drawer = useRef<HTMLElement>(null);
  useEffect(() => {
    if (!isOpen) return;
    const previous = document.activeElement as HTMLElement | null;
    const query = 'a[href],button:not([disabled])';
    drawer.current?.querySelector<HTMLElement>(query)?.focus();
    const keydown = (event: KeyboardEvent) => {
      if (event.key === "Escape") { onClose(); return; }
      if (event.key !== "Tab") return;
      const items = Array.from(drawer.current?.querySelectorAll<HTMLElement>(query) || []).filter(el => el.getClientRects().length);
      const first = items[0], last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    };
    const overflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", keydown);
    return () => { window.removeEventListener("keydown", keydown); document.body.style.overflow = overflow; previous?.focus(); };
  }, [isOpen, onClose]);
  return <>
    {isOpen && <div className="fixed inset-0 z-40 bg-black/40 lg:hidden" onClick={onClose} aria-hidden="true" />}
    <aside ref={drawer} aria-label="Workspace navigation" className={`fixed inset-y-0 left-0 z-50 w-56 bg-sidebar border-r border-border flex flex-col lg:translate-x-0 ${isOpen ? "translate-x-0" : "-translate-x-full invisible lg:visible"}`}>
      <div className="h-16 flex items-center justify-between px-5 border-b border-border">
        <Link href="/archives" onClick={onClose} className="flex items-center gap-2.5 text-base font-semibold tracking-tight"><svg viewBox="0 0 28 28" className="w-7 h-7 shrink-0 text-accent" fill="none" aria-hidden="true"><circle cx="12" cy="16" r="8" stroke="currentColor" strokeWidth="1.75" /><path d="M17.5 10.5 23 5" stroke="currentColor" strokeWidth="1.75" /><circle cx="23" cy="5" r="3" fill="currentColor" /><circle cx="12" cy="16" r="2" fill="currentColor" /></svg>OrionSearch</Link>
        <button onClick={onClose} aria-label="Close sidebar" className="lg:hidden p-2 -mr-3 rounded-control"><X className="w-4 h-4" /></button>
      </div>
      <nav aria-label="Main" className="p-3 pt-7 space-y-1">
        <p className="px-3 mb-3 text-xs text-content-muted">Workspace</p>
        <Link href="/archives" onClick={onClose} aria-current={pathname.startsWith("/archives") ? "page" : undefined} className={`flex items-center gap-3 px-3 py-2.5 rounded-control text-sm font-medium ${pathname.startsWith("/archives") ? "bg-surface text-content-primary" : "text-content-secondary hover:bg-surface"}`}><FolderArchive className="w-4 h-4 text-accent" />Archives</Link>
        <Link href="/archives/new" onClick={onClose} className="flex items-center gap-3 px-3 py-2.5 rounded-control text-sm text-content-secondary hover:bg-surface"><Plus className="w-4 h-4" />New archive</Link>
      </nav>
      <div className="mt-auto p-5 border-t border-border space-y-4">
        <Link href="/settings" onClick={onClose} className="flex items-center gap-3 text-sm text-content-secondary hover:text-content-primary"><Settings className="w-4 h-4" />Settings</Link>
        <a href="https://github.com/BolyarCoders/OrionSearch" target="_blank" rel="noreferrer" className="flex items-center gap-3 text-xs text-content-muted hover:text-content-primary">Help & Docs<ArrowUpRight className="w-3.5 h-3.5" /></a>
      </div>
    </aside>
  </>;
}

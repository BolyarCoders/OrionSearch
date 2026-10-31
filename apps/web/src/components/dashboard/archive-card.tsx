"use client";

import React, { useState } from "react";
import Link from "next/link";

import { ArchiveSummary } from "@/types/api";
import { StatusPill } from "../ui/status-pill";
import { formatRelativeTime } from "@/lib/normalization";
import { FileText, MoreVertical, Eye, PauseCircle, PlayCircle, Archive as ArchiveIcon } from "lucide-react";

interface ArchiveCardProps {
  archive: ArchiveSummary;
  onStatusChange?: (id: string, newStatus: "active" | "paused" | "archived") => void;
}

export function ArchiveCard({ archive, onStatusChange }: ArchiveCardProps) {
  const [menuOpen, setMenuOpen] = useState(false);


  return (
    <div className="dossier group flex flex-col min-h-[252px]">
      <div className="relative h-12 flex items-center px-5">
        <span className="text-xs text-content-muted">Research archive</span>
        {/* Card Options Menu Button */}
        <div className="absolute top-2.5 right-2.5 z-10">
          <button
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();
              setMenuOpen(!menuOpen);
            }}
            className="p-2 rounded-control text-content-secondary hover:bg-surface-raised"
            aria-label="Archive options" aria-expanded={menuOpen} onKeyDown={e => { if (e.key === "Escape") setMenuOpen(false); }}
          >
            <MoreVertical className="w-4 h-4" />
          </button>

          {menuOpen && (
            <>
              <div
                className="fixed inset-0 z-20"
                onClick={(e) => {
                  e.stopPropagation();
                  setMenuOpen(false);
                }}
              />
              <div className="absolute right-0 mt-1.5 w-44 bg-surface-raised border border-border rounded-control shadow-xl py-1 z-30 text-xs">
                <Link
                  href={`/archives/${archive.id}`}
                  className="flex items-center gap-2 px-3 py-2 text-content-primary hover:bg-surface transition-colors duration-150"
                  onClick={() => setMenuOpen(false)}
                >
                  <Eye className="w-3.5 h-3.5 text-content-muted" />
                  <span>View Details</span>
                </Link>

                {onStatusChange && archive.role === "owner" && archive.status !== "archived" && (
                  <>
                    {archive.status === "active" ? (
                      <button
                        onClick={() => {
                          setMenuOpen(false);
                          onStatusChange(archive.id, "paused");
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-content-primary hover:bg-surface transition-colors duration-150 text-left"
                      >
                        <PauseCircle className="w-3.5 h-3.5 text-status-warning" />
                        <span>Pause Archive</span>
                      </button>
                    ) : (
                      <button
                        onClick={() => {
                          setMenuOpen(false);
                          onStatusChange(archive.id, "active");
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-content-primary hover:bg-surface transition-colors duration-150 text-left"
                      >
                        <PlayCircle className="w-3.5 h-3.5 text-status-success" />
                        <span>Activate Archive</span>
                      </button>
                    )}

                    <button
                      onClick={() => {
                        setMenuOpen(false);
                        onStatusChange(archive.id, "archived");
                      }}
                      className="w-full flex items-center gap-2 px-3 py-2 text-status-failure hover:bg-surface transition-colors duration-150 text-left border-t border-border/80"
                    >
                      <ArchiveIcon className="w-3.5 h-3.5" />
                      <span>Archive (Soft Delete)</span>
                    </button>
                  </>
                )}
              </div>
            </>
          )}
        </div>
      </div>

      {/* Card Body */}
      <div className="px-5 pb-5 pt-1 flex-1 flex flex-col justify-between">
        <div>
          <Link href={`/archives/${archive.id}`}>
            <h3 className="text-lg leading-snug dossier-title font-semibold text-content-primary hover:text-accent transition-colors duration-150 line-clamp-2">
              {archive.title}
            </h3>
          </Link>
          <p className="text-sm text-content-secondary mt-2 line-clamp-2 leading-relaxed">
            {archive.description || "No topic description provided."}
          </p>
        </div>

        {/* Card Footer */}
        <div className="mt-5 pt-4 border-t border-border/70 flex flex-wrap gap-3 items-center justify-between text-xs text-content-muted">
          <div className="flex items-center gap-1.5 font-medium text-content-secondary">
            <FileText className="w-3.5 h-3.5 text-content-muted" />
            <span>{archive.sourceCount.toLocaleString()} sources</span>
          </div>

          <div className="flex items-center gap-2.5">
            <StatusPill status={archive.status} />

          </div>
        </div>
        <p className="text-xs text-content-muted mt-3" title={archive.updatedAt}>Updated {formatRelativeTime(archive.updatedAt)}</p>
      </div>
    </div>
  );
}

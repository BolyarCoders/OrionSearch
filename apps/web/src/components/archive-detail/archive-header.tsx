"use client";

import React, { useState } from "react";

import Link from "next/link";
import { ArchiveDetails } from "@/types/api";
import { StatusPill } from "../ui/status-pill";
import { Button } from "../ui/button";

import {
  ChevronRight,
  Play,
  MoreVertical,
  PauseCircle,
  PlayCircle,
  Archive as ArchiveIcon,
} from "lucide-react";

interface ArchiveHeaderProps {
  details: ArchiveDetails;
  onStartResearch: () => void;
  onStatusChange: (status: "active" | "paused" | "archived") => void;
}

export function ArchiveHeader({ details, onStartResearch, onStatusChange }: ArchiveHeaderProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const { archive } = details;


  return (
    <div className="space-y-4">
      {/* Breadcrumb */}
      <nav className="flex items-center gap-2 text-xs text-content-muted">
        <Link href="/archives" className="hover:text-content-primary transition-colors">
          Archives
        </Link>
        <ChevronRight className="w-3.5 h-3.5" />
        <span className="text-content-primary font-medium truncate max-w-xs">{archive.title}</span>
      </nav>

      {/* Understated Cropped Banner & Main Header Card */}
      <div className="border-b border-border pb-6">
        {/* Content Bar */}
        <div className="py-3 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="library-title text-content-primary">
                {archive.title}
              </h1>
              <StatusPill status={archive.status} />
              <span className="text-xs text-content-muted capitalize bg-surface-raised px-2 py-0.5 rounded border border-border">
                {archive.role}
              </span>
            </div>
            <p className="text-xs sm:text-sm text-content-secondary max-w-2xl leading-relaxed">
              {archive.description || "No topic description provided for this archive."}
            </p>
          </div>

          {/* Action buttons */}
          <div className="flex items-center gap-2.5 shrink-0 w-full sm:w-auto">
            <Button
              variant="primary"
              size="sm"
              leftIcon={<Play className="w-4 h-4" />}
              onClick={onStartResearch}
              disabled={archive.status !== "active" || archive.role === "viewer"}
              title={
                archive.status !== "active"
                  ? "Research can only be initiated on active archives"
                  : "Start research run"
              }
            >
              Start research
            </Button>

            {/* Owner Lifecycle Actions */}
            <div className="relative" hidden={archive.role !== "owner"}>
              <button
                onClick={() => setMenuOpen(!menuOpen)}
                className="p-2 rounded-control bg-surface-raised border border-border text-content-secondary hover:text-content-primary hover:border-border-light transition-colors"
                aria-label="More archive options"
              >
                <MoreVertical className="w-4 h-4" />
              </button>

              {menuOpen && (
                <>
                  <div className="fixed inset-0 z-20" onClick={() => setMenuOpen(false)} />
                  <div className="absolute right-0 mt-1.5 w-44 bg-surface-raised border border-border rounded-control shadow-xl py-1 z-30 text-xs">
                    {archive.status === "active" ? (
                      <button
                        onClick={() => {
                          setMenuOpen(false);
                          onStatusChange("paused");
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-content-primary hover:bg-surface transition-colors text-left"
                      >
                        <PauseCircle className="w-3.5 h-3.5 text-status-warning" />
                        <span>Pause Archive</span>
                      </button>
                    ) : (
                      <button
                        onClick={() => {
                          setMenuOpen(false);
                          onStatusChange("active");
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-content-primary hover:bg-surface transition-colors text-left"
                      >
                        <PlayCircle className="w-3.5 h-3.5 text-status-success" />
                        <span>Activate Archive</span>
                      </button>
                    )}

                    {archive.status !== "archived" && (
                      <button
                        onClick={() => {
                          setMenuOpen(false);
                          onStatusChange("archived");
                        }}
                        className="w-full flex items-center gap-2 px-3 py-2 text-status-failure hover:bg-surface transition-colors text-left border-t border-border/80"
                      >
                        <ArchiveIcon className="w-3.5 h-3.5" />
                        <span>Archive (Soft Delete)</span>
                      </button>
                    )}
                  </div>
                </>
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

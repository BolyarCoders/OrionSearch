import React from "react";
import { ArchiveStatus, ResearchRunState } from "@/types/api";

export interface StatusPillProps {
  status: ArchiveStatus | ResearchRunState;
  className?: string;
  showIcon?: boolean;
}

export function StatusPill({ status, className = "", showIcon = true }: StatusPillProps) {
  const configMap: Record<
    ArchiveStatus | ResearchRunState,
    { label: string; dotClass: string; bgClass: string; textClass: string; pulse?: boolean }
  > = {
    active: {
      label: "Active",
      dotClass: "bg-status-success",
      bgClass: "bg-status-success/10 border-status-success/20",
      textClass: "text-status-success",
    },
    paused: {
      label: "Paused",
      dotClass: "bg-status-warning",
      bgClass: "bg-status-warning/10 border-status-warning/20",
      textClass: "text-status-warning",
    },
    archived: {
      label: "Archived",
      dotClass: "bg-content-muted",
      bgClass: "bg-surface-raised border-border",
      textClass: "text-content-muted",
    },
    queued: {
      label: "Queued",
      dotClass: "bg-status-info",
      bgClass: "bg-status-info/10 border-status-info/20",
      textClass: "text-status-info",
    },
    running: {
      label: "Running",
      dotClass: "bg-accent",
      bgClass: "bg-accent/15 border-accent/30",
      textClass: "text-accent",
      pulse: true,
    },
    completed: {
      label: "Completed",
      dotClass: "bg-status-success",
      bgClass: "bg-status-success/10 border-status-success/20",
      textClass: "text-status-success",
    },
    failed: {
      label: "Failed",
      dotClass: "bg-status-failure",
      bgClass: "bg-status-failure/10 border-status-failure/20",
      textClass: "text-status-failure",
    },
    cancelled: {
      label: "Cancelled",
      dotClass: "bg-content-muted",
      bgClass: "bg-surface-raised border-border",
      textClass: "text-content-muted",
    },
  };

  const item = configMap[status] || {
    label: status,
    dotClass: "bg-content-muted",
    bgClass: "bg-surface-raised border-border",
    textClass: "text-content-muted",
  };

  return (
    <span
      className={`inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-medium border ${item.bgClass} ${item.textClass} ${className}`}
    >
      {showIcon && (
        <span
          className={`w-1.5 h-1.5 rounded-full ${item.dotClass} ${item.pulse ? "animate-pulse" : ""}`}
          aria-hidden="true"
        />
      )}
      <span>{item.label}</span>
    </span>
  );
}

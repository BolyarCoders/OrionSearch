"use client";

import React from "react";
import Link from "next/link";
import { useAppMode } from "@/lib/context/app-mode-context";
import { DEMO_RECENT_ACTIVITY } from "@/lib/api/mock-data";
import { StatusPill } from "../ui/status-pill";
import { formatRelativeTime } from "@/lib/normalization";
import { Activity, ArrowRight, Info, Brain, Atom, Rocket, Leaf } from "lucide-react";

export function RecentActivity() {
  const { isLive } = useAppMode();

  const getArchiveIcon = (title: string) => {
    switch (title) {
      case "Artificial Intelligence":
        return Brain;
      case "Quantum Computing":
        return Atom;
      case "Space Exploration":
        return Rocket;
      case "Climate Policy":
        return Leaf;
      default:
        return Activity;
    }
  };

  return (
    <div className="bg-surface border border-border rounded-panel p-5 mt-8">
      {/* Header */}
      <div className="flex items-center justify-between pb-4 border-b border-border/80">
        <div className="flex items-center gap-2">
          <Activity className="w-4 h-4 text-accent" />
          <h3 className="text-sm font-semibold text-content-primary">Recent research activity</h3>
          {!isLive && (
            <span className="text-xs bg-accent/15 text-accent px-2 py-0.5 rounded-full font-medium">
              Demo Preview
            </span>
          )}
        </div>

      </div>

      {isLive ? (
        <div className="py-8 px-4 text-center max-w-lg mx-auto">
          <div className="w-9 h-9 rounded-full bg-surface-raised border border-border flex items-center justify-center mx-auto text-content-muted mb-3">
            <Info className="w-4 h-4 text-accent" />
          </div>
          <h4 className="text-xs font-semibold text-content-primary">
            Follow research inside each archive
          </h4>
          <p className="text-xs text-content-muted mt-1 leading-relaxed">Open an archive’s Research tab to follow a run. A library-wide activity feed is not available.</p>
        </div>
      ) : (
        <div className="overflow-x-auto mt-2">
          <table className="w-full text-left text-xs">
            <thead>
              <tr className="text-content-muted border-b border-border/60">
                <th className="py-3 px-3 font-medium">Archive</th>
                <th className="py-3 px-3 font-medium">Run Objective</th>
                <th className="py-3 px-3 font-medium">Status</th>
                <th className="py-3 px-3 font-medium text-right">Sources</th>
                <th className="py-3 px-3 font-medium text-right">Findings</th>
                <th className="py-3 px-3 font-medium text-right">Started</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border/40">
              {DEMO_RECENT_ACTIVITY.map((item) => {
                const Icon = getArchiveIcon(item.archiveTitle);
                return (
                  <tr key={item.id} className="hover:bg-surface-raised/40 transition-colors">
                    <td className="py-3 px-3 font-medium text-content-primary">
                      <Link
                        href={`/archives/${item.archiveId}`}
                        className="flex items-center gap-2 hover:text-accent transition-colors"
                      >
                        <Icon className="w-3.5 h-3.5 text-content-muted" />
                        <span>{item.archiveTitle}</span>
                      </Link>
                    </td>
                    <td className="py-3 px-3 text-content-secondary max-w-xs truncate">
                      {item.runTitle}
                    </td>
                    <td className="py-3 px-3">
                      <StatusPill status={item.status} />
                    </td>
                    <td className="py-3 px-3 text-right text-content-secondary font-mono">
                      {item.sources !== null ? item.sources : "—"}
                    </td>
                    <td className="py-3 px-3 text-right text-content-secondary font-mono">
                      {item.findings !== null ? item.findings : "—"}
                    </td>
                    <td className="py-3 px-3 text-right text-content-muted">
                      {formatRelativeTime(item.startedAt)}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

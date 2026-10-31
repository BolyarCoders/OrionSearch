import React from "react";
import { ArchiveSummary } from "@/types/api";

export function StatSummary({ archives, totalCount }: { archives: ArchiveSummary[]; totalCount: number }) {
  const stats = [
    ["Matching archives", totalCount],
    ["Active (visible)", archives.filter(a => a.status === "active").length],
    ["Paused (visible)", archives.filter(a => a.status === "paused").length],
    ["Indexed sources (visible)", archives.reduce((n, a) => n + (a.sourceCount || 0), 0).toLocaleString()],
  ];
  return <dl className="grid grid-cols-2 xl:grid-cols-4 border-y border-border py-5 gap-x-6 gap-y-4">
    {stats.map(([label, value]) => <div key={label} className="flex flex-col-reverse gap-1"><dt className="text-xs text-content-muted">{label}</dt><dd className="text-2xl font-medium tracking-tight tabular-nums">{value}</dd></div>)}
  </dl>;
}
